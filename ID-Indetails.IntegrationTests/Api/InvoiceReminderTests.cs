using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Clients;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Billing.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Reminders of overdue invoices, emailed by the hourly run (called directly here).</summary>
public class InvoiceReminderTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Beirut"));

    private async Task RemindersAsync(string token, bool enabled, int firstAfter = 1, int every = 7, int max = 2)
    {
        var response = await Client.PutJsonAsync($"/api/billing-settings/{DomainCodes.Design}", new
        {
            invoicePrefix = "HL",
            defaultTermsDays = 30,
            remindersEnabled = enabled,
            reminderFirstAfterDays = firstAfter,
            reminderEveryDays = every,
            reminderMax = max,
        }, token);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    /// <summary>An invoice issued 20 days ago on 10-day terms: 10 days overdue.</summary>
    private async Task<InvoiceResponse> OverdueInvoiceAsync(long projectId, string token, decimal amount = 1000m)
    {
        var draft = await (await Client.PostJsonAsync($"/api/projects/{projectId}/invoices",
            new { lines = new[] { new { description = "Stage", amount } }, termsDays = 10 }, token)).ReadAsync<InvoiceResponse>();
        var issued = await Client.PostJsonAsync($"/api/invoices/{draft.Id}/issue", new { issuedOn = Today.AddDays(-20) }, token);
        issued.IsSuccessStatusCode.ShouldBeTrue(await issued.Content.ReadAsStringAsync());
        return await issued.ReadAsync<InvoiceResponse>();
    }

    private async Task<InvoiceResponse> InvoiceAsync(long id, string token)
        => await (await Client.GetAsync($"/api/invoices/{id}", token)).ReadAsync<InvoiceResponse>();

    private async Task RunAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInvoiceReminderService>().RunAsync();
    }

    /// <summary>As if the invoice's reminders were sent a week and a day earlier.</summary>
    private Task BackdateRemindersAsync(long invoiceId)
        => _steps.ExecuteAsync(
            "UPDATE invoice_deliveries SET sent_at = DATE_SUB(sent_at, INTERVAL 8 DAY) WHERE invoice_id = @id AND is_reminder = 1", ("@id", invoiceId));

    private async Task<string> ClientEmailAsync(ProjectResponse project, string token)
        => (await (await Client.GetAsync($"/api/clients/{project.Client!.Id}", token)).ReadAsync<ClientResponse>()).Contacts.First(c => c.Kind == ContactKind.Email).Value;

    [Fact]
    public async Task An_overdue_invoice_is_reminded_on_schedule_up_to_the_most_reminders()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var project = await Projects.CreateProjectAsync(token, DomainCodes.Design, revenue: 10_000m);
        await RemindersAsync(token, enabled: true, max: 2);
        var invoice = await OverdueInvoiceAsync(project.Id, token);
        var to = await ClientEmailAsync(project, token);

        await RunAsync();
        await RunAsync();

        api.Emails.CountTo(to).ShouldBe(1);
        var mail = api.Emails.LastTo(to);
        mail.Subject.ShouldBe($"Reminder: invoice {invoice.Number} - {project.Title}");
        mail.TextBody.ShouldContain("still to be paid");
        mail.Attachments.ShouldNotBeNull().ShouldHaveSingleItem().FileName.ShouldBe($"{invoice.Number!.ToLowerInvariant()}.pdf");
        var reminder = (await InvoiceAsync(invoice.Id, token)).Deliveries.ShouldHaveSingleItem();
        reminder.IsReminder.ShouldBeTrue();
        reminder.Note.ShouldBe("Automatic reminder 1 of 2");

        // a week later the second, and then no more
        await BackdateRemindersAsync(invoice.Id);
        await RunAsync();
        api.Emails.CountTo(to).ShouldBe(2);
        await BackdateRemindersAsync(invoice.Id);
        await RunAsync();
        api.Emails.CountTo(to).ShouldBe(2);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE kind = 'invoice_sent' AND subject_id = @id AND JSON_EXTRACT(payload, '$.reminder') = 2",
            ("@id", invoice.Id))).ShouldBe(1);
    }

    [Fact]
    public async Task A_reminder_goes_where_the_invoice_was_emailed_and_stops_once_it_is_paid()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var project = await Projects.CreateProjectAsync(token, DomainCodes.Design, revenue: 10_000m);
        await RemindersAsync(token, enabled: true, max: 3);
        var invoice = await OverdueInvoiceAsync(project.Id, token);
        var accounts = AuthTestSteps.NewEmail("accounts");
        (await Client.PostJsonAsync($"/api/invoices/{invoice.Id}/email", new { to = accounts }, token)).IsSuccessStatusCode.ShouldBeTrue();

        await RunAsync();
        api.Emails.CountTo(accounts).ShouldBe(2);
        api.Emails.LastTo(accounts).Subject.ShouldStartWith("Reminder:");

        (await Client.PostJsonAsync($"/api/projects/{project.Id}/payments", new { amount = 1000m, currency = "USD", invoiceId = invoice.Id }, token))
            .IsSuccessStatusCode.ShouldBeTrue();
        await BackdateRemindersAsync(invoice.Id);
        await RunAsync();
        api.Emails.CountTo(accounts).ShouldBe(2);
    }

    [Fact]
    public async Task Nothing_is_sent_with_reminders_off_and_with_no_email_whoever_sends_invoices_is_asked_once()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var project = await Projects.CreateProjectAsync(token, DomainCodes.Design, revenue: 10_000m);
        await RemindersAsync(token, enabled: false);
        var invoice = await OverdueInvoiceAsync(project.Id, token);
        var to = await ClientEmailAsync(project, token);

        await RunAsync();
        api.Emails.CountTo(to).ShouldBe(0);

        // a client reached only by phone
        var phoneOnly = await (await Client.PostJsonAsync("/api/clients", new
        {
            name = "Client " + Guid.NewGuid().ToString("N")[..8],
            label = "Client",
            domainCode = DomainCodes.Design,
            contacts = new[] { new { kind = "phone", value = "+961 3 555 142", isWhatsapp = true } },
        }, token)).ReadAsync<ClientResponse>();
        var site = await (await Client.PostJsonAsync("/api/projects", new
        {
            title = "Project " + Guid.NewGuid().ToString("N")[..8], domainCode = DomainCodes.Design, clientId = phoneOnly.Id, revenue = 5000m,
        }, token)).ReadAsync<ProjectResponse>();
        var unreachable = await OverdueInvoiceAsync(site.Id, token);
        await RemindersAsync(token, enabled: true);

        await RunAsync();
        await RunAsync();

        (await InvoiceAsync(unreachable.Id, token)).Deliveries.ShouldBeEmpty();
        var asked = (await (await Client.GetAsync("/api/notifications?pageSize=100", token)).ReadAsync<PagedResponse<NotificationResponse>>())
            .Items.Where(n => n.Kind == NotificationKinds.InvoiceReminderDue).ToList();
        asked.ShouldHaveSingleItem().SubjectId.ShouldBe(unreachable.Id);
        // the first project's invoice, reminded now that they are on
        (await InvoiceAsync(invoice.Id, token)).Deliveries.ShouldHaveSingleItem().IsReminder.ShouldBeTrue();
    }

    [Fact]
    public async Task Reminder_settings_are_kept_within_bounds()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await (await Client.PutJsonAsync($"/api/billing-settings/{DomainCodes.Design}",
                new { invoicePrefix = "HL", defaultTermsDays = 30, remindersEnabled = true, reminderFirstAfterDays = 0, reminderEveryDays = 7, reminderMax = 3 },
                superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PutJsonAsync($"/api/billing-settings/{DomainCodes.Design}",
                new { invoicePrefix = "HL", defaultTermsDays = 30, remindersEnabled = true, reminderFirstAfterDays = 1, reminderEveryDays = 7, reminderMax = 11 },
                superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        await RemindersAsync(superAdmin.AccessToken, enabled: true, firstAfter: 3, every: 14, max: 4);
        var saved = await (await Client.GetAsync($"/api/billing-settings/{DomainCodes.Design}", superAdmin.AccessToken)).ReadAsync<BillingSettingsResponse>();
        (saved.RemindersEnabled, saved.ReminderFirstAfterDays, saved.ReminderEveryDays, saved.ReminderMax).ShouldBe((true, 3, 14, 4));
    }
}
