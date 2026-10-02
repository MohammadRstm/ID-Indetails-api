using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Auto invoice: drafts that are issued and emailed on their own days, by the hourly run (called directly here).</summary>
public class InvoiceScheduleTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Beirut"));

    private async Task<(AuthResponse SuperAdmin, ProjectResponse Project)> BillableProjectAsync(decimal revenue = 9000m)
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue);
        await Projects.SetUpBillingAsync(superAdmin.AccessToken, DomainCodes.Design);
        return (superAdmin, project);
    }

    private static object Plan(string? sendTo, params (int InDays, decimal Amount)[] invoices) => new
    {
        sendTo,
        invoices = invoices.Select((p, i) => new { sendOn = Today.AddDays(p.InDays), amount = p.Amount, description = $"Payment {i + 1} of {invoices.Length}" }),
    };

    private async Task<List<InvoiceResponse>> PlanAsync(long projectId, string token, object plan)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/invoice-plan", plan, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<List<InvoiceResponse>>();
    }

    private async Task<InvoiceResponse> InvoiceAsync(long id, string token)
        => await (await Client.GetAsync($"/api/invoices/{id}", token)).ReadAsync<InvoiceResponse>();

    /// <summary>Its day moved into the past, so the hourly run sends it whatever the time of day.</summary>
    private Task DueYesterdayAsync(long invoiceId)
        => _steps.ExecuteAsync("UPDATE invoices SET send_on = @d WHERE id = @id", ("@d", Today.AddDays(-1)), ("@id", invoiceId));

    private async Task RunAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInvoiceScheduleService>().RunAsync();
    }

    private async Task<List<NotificationResponse>> OfKindAsync(AuthResponse person, string kind)
        => (await (await Client.GetAsync("/api/notifications?pageSize=100", person.AccessToken)).ReadAsync<PagedResponse<NotificationResponse>>())
            .Items.Where(n => n.Kind == kind).ToList();

    [Fact]
    public async Task A_plan_schedules_its_drafts_and_sends_the_one_dated_today_at_once()
    {
        var (superAdmin, project) = await BillableProjectAsync();
        var to = AuthTestSteps.NewEmail("accounts");

        var plan = await PlanAsync(project.Id, superAdmin.AccessToken, Plan(to, (0, 3000m), (30, 3000m), (60, 3000m)));

        plan.Count.ShouldBe(3);
        var first = plan[0];
        first.Status.ShouldBe(InvoiceStatus.Sent);
        first.Number.ShouldNotBeNull();
        first.SendOn.ShouldBeNull();
        first.Deliveries.ShouldHaveSingleItem().Recipient.ShouldBe(to);
        api.Emails.CountTo(to).ShouldBe(1);
        api.Emails.LastTo(to).Subject.ShouldContain(first.Number!);
        foreach (var later in plan.Skip(1))
        {
            later.Status.ShouldBe(InvoiceStatus.Draft);
            later.SendTo.ShouldBe(to);
            later.ScheduledBy!.Id.ShouldBe(superAdmin.User.Id);
            later.Lines.ShouldHaveSingleItem().Description.ShouldStartWith("Payment ");
        }

        plan.Select(i => i.SendOn).ShouldBe([null, Today.AddDays(30), Today.AddDays(60)]);
        (await OfKindAsync(superAdmin, NotificationKinds.InvoiceSentAutomatically)).ShouldHaveSingleItem()
            .Payload!.Value.GetProperty("to").GetString().ShouldBe(to);
        var billing = (await (await Client.GetAsync($"/api/projects/{project.Id}", superAdmin.AccessToken)).ReadAsync<ProjectResponse>()).Money!.Billing;
        billing.Invoiced.ShouldBe(3000m);
        billing.Drafts.ShouldBe(6000m);
    }

    [Fact]
    public async Task The_hourly_run_sends_a_draft_once_its_day_has_come_and_only_once()
    {
        var (superAdmin, project) = await BillableProjectAsync();
        var to = AuthTestSteps.NewEmail("accounts");
        var plan = await PlanAsync(project.Id, superAdmin.AccessToken, Plan(to, (1, 4000m), (2, 5000m)));

        await RunAsync();
        api.Emails.CountTo(to).ShouldBe(0);

        await DueYesterdayAsync(plan[0].Id);
        await RunAsync();
        await RunAsync();

        var sent = await InvoiceAsync(plan[0].Id, superAdmin.AccessToken);
        sent.Status.ShouldBe(InvoiceStatus.Sent);
        sent.SendOn.ShouldBeNull();
        sent.Deliveries.ShouldHaveSingleItem().Note.ShouldBe("Sent automatically, as scheduled");
        api.Emails.CountTo(to).ShouldBe(1);
        (await InvoiceAsync(plan[1].Id, superAdmin.AccessToken)).Status.ShouldBe(InvoiceStatus.Draft);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE kind = 'invoice_sent' AND subject_id = @id AND JSON_EXTRACT(payload, '$.automatic') = true",
            ("@id", plan[0].Id))).ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_email_leaves_it_a_scheduled_draft_tried_again_and_says_so_once()
    {
        var (superAdmin, project) = await BillableProjectAsync();
        var to = AuthTestSteps.NewEmail("accounts");
        var plan = await PlanAsync(project.Id, superAdmin.AccessToken, Plan(to, (1, 4000m), (2, 5000m)));
        await DueYesterdayAsync(plan[0].Id);

        api.Emails.FailNextSend = true;
        await RunAsync();

        var waiting = await InvoiceAsync(plan[0].Id, superAdmin.AccessToken);
        waiting.Status.ShouldBe(InvoiceStatus.Draft);
        waiting.Number.ShouldBeNull();
        waiting.SendOn.ShouldBe(Today.AddDays(-1));
        (await OfKindAsync(superAdmin, NotificationKinds.InvoiceSendFailed)).ShouldHaveSingleItem().SubjectId.ShouldBe(plan[0].Id);

        await RunAsync();
        (await InvoiceAsync(plan[0].Id, superAdmin.AccessToken)).Status.ShouldBe(InvoiceStatus.Sent);
        api.Emails.CountTo(to).ShouldBe(1);
        (await OfKindAsync(superAdmin, NotificationKinds.InvoiceSendFailed)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task One_already_paid_on_account_is_issued_but_not_emailed()
    {
        var (superAdmin, project) = await BillableProjectAsync();
        var to = AuthTestSteps.NewEmail("accounts");
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 4000m, currency = "USD" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var plan = await PlanAsync(project.Id, superAdmin.AccessToken, Plan(to, (1, 4000m), (2, 5000m)));
        await DueYesterdayAsync(plan[0].Id);

        await RunAsync();

        var paid = await InvoiceAsync(plan[0].Id, superAdmin.AccessToken);
        paid.Status.ShouldBe(InvoiceStatus.Paid);
        paid.Deliveries.ShouldBeEmpty();
        api.Emails.CountTo(to).ShouldBe(0);
        (await OfKindAsync(superAdmin, NotificationKinds.InvoiceSentAutomatically)).ShouldHaveSingleItem()
            .Payload!.Value.GetProperty("to").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Without_an_address_whoever_scheduled_it_is_told_once_to_send_it()
    {
        var (superAdmin, project) = await BillableProjectAsync();
        var plan = await PlanAsync(project.Id, superAdmin.AccessToken, Plan(null, (1, 4000m), (2, 5000m)));
        await DueYesterdayAsync(plan[0].Id);

        await RunAsync();
        await RunAsync();

        (await InvoiceAsync(plan[0].Id, superAdmin.AccessToken)).Status.ShouldBe(InvoiceStatus.Draft);
        (await OfKindAsync(superAdmin, NotificationKinds.InvoiceSendDue)).ShouldHaveSingleItem().SubjectId.ShouldBe(plan[0].Id);
    }

    [Fact]
    public async Task A_schedule_is_moved_or_cancelled_and_ends_when_the_draft_is_sent_by_hand()
    {
        var (superAdmin, project) = await BillableProjectAsync();
        var token = superAdmin.AccessToken;
        var plan = await PlanAsync(project.Id, token, Plan(AuthTestSteps.NewEmail("accounts"), (5, 4000m), (6, 5000m)));

        var moved = await (await Client.PutJsonAsync($"/api/invoices/{plan[0].Id}/schedule", new { sendOn = Today.AddDays(10), sendTo = "new@client.test" }, token))
            .ReadAsync<InvoiceResponse>();
        moved.SendOn.ShouldBe(Today.AddDays(10));
        moved.SendTo.ShouldBe("new@client.test");
        await (await Client.PutJsonAsync($"/api/invoices/{plan[0].Id}/schedule", new { sendOn = Today.AddDays(-1) }, token))
            .ShouldFailWithAsync(400, ErrorCodes.SendDateInPast);

        var cancelled = await (await Client.DeleteAsync($"/api/invoices/{plan[0].Id}/schedule", token)).ReadAsync<InvoiceResponse>();
        cancelled.SendOn.ShouldBeNull();
        cancelled.SendTo.ShouldBeNull();
        cancelled.Status.ShouldBe(InvoiceStatus.Draft);

        var issued = await (await Client.PostEmptyAsync($"/api/invoices/{plan[1].Id}/issue", token)).ReadAsync<InvoiceResponse>();
        issued.SendOn.ShouldBeNull();
        // the database holds to it too: only a draft is scheduled
        await Should.ThrowAsync<MySqlConnector.MySqlException>(() => DueYesterdayAsync(plan[1].Id));
    }

    [Fact]
    public async Task A_plan_needs_two_invoices_days_from_today_billing_settings_and_both_invoice_permissions()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var project = await Projects.CreateProjectAsync(token, DomainCodes.Execution);
        var url = $"/api/projects/{project.Id}/invoice-plan";

        // invoices can't be issued in a domain before its billing settings exist
        await (await Client.PostJsonAsync(url, Plan(null, (1, 500m), (2, 500m)), token)).ShouldFailWithAsync(422, ErrorCodes.BillingSettingsMissing);
        await Projects.SetUpBillingAsync(token, DomainCodes.Execution);
        await (await Client.PostJsonAsync(url, Plan(null, (1, 500m)), token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync(url, Plan(null, (-1, 500m), (2, 500m)), token)).ShouldFailWithAsync(400, ErrorCodes.SendDateInPast);
        await (await Client.PostJsonAsync(url, Plan("not an address", (1, 500m), (2, 500m)), token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        await (await Client.PostJsonAsync(url, Plan(null, (1, 500m), (2, 500m)), employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task A_scheduled_draft_isnt_flagged_as_waiting_before_its_day()
    {
        var (superAdmin, project) = await BillableProjectAsync();
        var plan = await PlanAsync(project.Id, superAdmin.AccessToken, Plan(null, (5, 4000m), (6, 5000m)));
        await _steps.ExecuteAsync("UPDATE invoices SET created_at = DATE_SUB(UTC_TIMESTAMP(3), INTERVAL 4 DAY) WHERE project_id = @p", ("@p", project.Id));

        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IScheduledNotifications>().RunAsync(ScheduledCheck.DraftInvoices);
        }

        (await OfKindAsync(superAdmin, NotificationKinds.InvoiceDraftWaiting)).ShouldNotContain(n => plan.Any(i => i.Id == n.SubjectId));
    }
}
