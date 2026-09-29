using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Activity;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Notifications.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The money insights and auto-archiving. Scheduled checks are run directly (the timer is off in tests), twice to
/// prove each notification goes once. Each test uses its own projects, so they can share the database.
/// </summary>
public class InsightNotificationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    private async Task RunTwiceAsync(ScheduledCheck check)
    {
        for (var i = 0; i < 2; i++)
        {
            await using var scope = api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IScheduledNotifications>().RunAsync(check);
        }
    }

    private Task SqlAsync(string sql, params (string Name, object Value)[] parameters)
        => _steps.ScalarAsync<int>(sql + "; SELECT 1", parameters);

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private async Task<List<NotificationResponse>> OfKindAsync(AuthResponse person, string kind, long projectId)
        => (await OkAsync<PagedResponse<NotificationResponse>>(Client.GetAsync("/api/notifications?pageSize=100", person.AccessToken)))
            .Items.Where(n => n.Kind == kind && n.Project?.Id == projectId).ToList();

    private async Task<ProjectStatus> StatusAsync(long projectId, string token)
        => (await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{projectId}", token))).Status;

    [Fact]
    public async Task A_cost_warning_goes_once_to_whoever_sees_the_money_when_cost_passes_85_percent_of_the_payment()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        // 1,733.30 a month is 10 an hour.
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1733.30m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var over = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 100m);
        var under = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 10_000m);

        // A full Tuesday on "over" (09:00-17:00 in Beirut): 80 plus the paid hour of break, 90 of its 100. An hour on "under".
        await Work.InsertAsync(designer.User.Id, over.Id, "2026-02-10 07:00:00", "2026-02-10 15:00:00");
        await Work.InsertAsync(designer.User.Id, under.Id, "2026-02-11 07:00:00", "2026-02-11 08:00:00");

        await RunTwiceAsync(ScheduledCheck.CostWarnings);

        var warning = (await OfKindAsync(admin, NotificationKinds.ProjectCostHigh, over.Id)).ShouldHaveSingleItem();
        warning.Actor.ShouldBeNull();
        warning.SubjectId.ShouldBe(over.Id);
        warning.Payload!.Value.GetProperty("usedPct").GetInt32().ShouldBe(90);
        warning.Payload.Value.TryGetProperty("cost", out _).ShouldBeFalse();
        (await OfKindAsync(admin, NotificationKinds.ProjectCostHigh, under.Id)).ShouldBeEmpty();
        (await OfKindAsync(designer, NotificationKinds.ProjectCostHigh, over.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_draft_invoice_still_unissued_after_three_days_is_flagged_once()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        async Task<InvoiceResponse> DraftAsync() => await OkAsync<InvoiceResponse>(Client.PostJsonAsync($"/api/projects/{project.Id}/invoices",
            new { lines = new[] { new { description = "Stage", amount = 400m } } }, admin.AccessToken));

        // Two drafts; only the one created 4 days ago is waiting.
        var waiting = await DraftAsync();
        await DraftAsync();
        await SqlAsync("UPDATE invoices SET created_at = DATE_SUB(UTC_TIMESTAMP(3), INTERVAL 4 DAY) WHERE id = @id", ("@id", waiting.Id));

        await RunTwiceAsync(ScheduledCheck.DraftInvoices);

        var reminders = await OfKindAsync(superAdmin, NotificationKinds.InvoiceDraftWaiting, project.Id);
        var reminder = reminders.ShouldHaveSingleItem();
        reminder.SubjectType.ShouldBe("invoice");
        reminder.SubjectId.ShouldBe(waiting.Id);
        reminder.Payload!.Value.GetProperty("projectTitle").GetString().ShouldBe(project.Title);
        reminder.Payload.Value.TryGetProperty("amount", out _).ShouldBeFalse();
        (await OfKindAsync(admin, NotificationKinds.InvoiceDraftWaiting, project.Id)).ShouldHaveSingleItem().SubjectId.ShouldBe(waiting.Id);
        (await OfKindAsync(employee, NotificationKinds.InvoiceDraftWaiting, project.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Closing_a_client_project_tells_whoever_sees_the_money_its_margin()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 1000m);
        var tooling = await OkAsync<ProjectResponse>(Client.PostJsonAsync("/api/projects",
            new { title = "Studio tooling", billingType = "internal" }, admin.AccessToken));
        (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "Print run", amount = 250m, currency = "USD" }, admin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PostEmptyAsync($"/api/projects/{tooling.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        // 750 profit on 1,000: 75% margin, as a percentage and never an amount.
        var closed = (await OfKindAsync(admin, NotificationKinds.ProjectClosed, project.Id)).ShouldHaveSingleItem();
        closed.Actor!.Id.ShouldBe(superAdmin.User.Id);
        closed.Payload!.Value.GetProperty("marginPct").GetDecimal().ShouldBe(75m);
        closed.Payload.Value.TryGetProperty("profit", out _).ShouldBeFalse();
        (await OfKindAsync(employee, NotificationKinds.ProjectClosed, project.Id)).ShouldBeEmpty();
        (await OfKindAsync(admin, NotificationKinds.ProjectClosed, tooling.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Auto_archive_archives_completed_projects_quiet_for_90_days_while_the_setting_is_on()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken, autoArchive: true);
        var quiet = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var stillBusy = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var recent = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        foreach (var project in new[] { quiet, stillBusy, recent, site })
        {
            (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        }

        // Completed 100 days ago with everything since then just as old, except that "stillBusy" had its completion
        // recorded today, so its latest activity is recent. Execution's auto-archive setting is off.
        foreach (var project in new[] { quiet, stillBusy, site })
        {
            await SqlAsync("UPDATE projects SET completed_at = DATE_SUB(UTC_TIMESTAMP(3), INTERVAL 100 DAY) WHERE id = @id", ("@id", project.Id));
            await SqlAsync("UPDATE activity_events SET occurred_at = DATE_SUB(UTC_TIMESTAMP(3), INTERVAL 100 DAY) WHERE project_id = @id AND (@keep = 0 OR kind <> 'project_completed')",
                ("@id", project.Id), ("@keep", project.Id == stillBusy.Id ? 1 : 0));
        }

        await RunTwiceAsync(ScheduledCheck.AutoArchive);

        (await StatusAsync(quiet.Id, superAdmin.AccessToken)).ShouldBe(ProjectStatus.Archived);
        (await StatusAsync(stillBusy.Id, superAdmin.AccessToken)).ShouldBe(ProjectStatus.Completed);
        (await StatusAsync(recent.Id, superAdmin.AccessToken)).ShouldBe(ProjectStatus.Completed);
        (await StatusAsync(site.Id, superAdmin.AccessToken)).ShouldBe(ProjectStatus.Completed);

        var feed = await OkAsync<PagedResponse<ActivityItemResponse>>(Client.GetAsync($"/api/activity?projectId={quiet.Id}&pageSize=100", superAdmin.AccessToken));
        var archived = feed.Items.Where(i => i.Kind == ActivityEventKinds.ProjectArchived).ShouldHaveSingleItem();
        archived.Actor.ShouldBeNull();
        archived.Payload!.Value.GetProperty("automatic").GetBoolean().ShouldBeTrue();
    }
}
