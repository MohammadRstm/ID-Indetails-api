using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.DTO.Response.Requirements;
using ID_Indetails.api.Services.Notifications.Abstraction;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The scheduled checks, run directly (the timer is off in tests). Each runs twice to prove it sends once.
/// </summary>
public class ScheduledNotificationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    /// <summary>Today in the company time zone (the default, as no company settings are saved in tests).</summary>
    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Beirut"));

    private async Task RunTwiceAsync(ScheduledCheck check)
    {
        for (var i = 0; i < 2; i++)
        {
            await using var scope = api.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IScheduledNotifications>().RunAsync(check);
        }
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await api.Database.OpenConnectionAsync();
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    private async Task<List<NotificationResponse>> OfKindAsync(AuthResponse person, string kind)
        => (await OkAsync<PagedResponse<NotificationResponse>>(Client.GetAsync("/api/notifications?pageSize=100", person.AccessToken)))
            .Items.Where(n => n.Kind == kind).ToList();

    [Fact]
    public async Task Everyone_who_logs_work_gets_last_weeks_summary_once()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, busy) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, quiet) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, siteWorker) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        // Three hours on the Tuesday of last week (the week starts on Monday by default): 09:00 to 12:00 in Beirut.
        var lastWeek = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7) - 7);
        var tuesday = lastWeek.AddDays(1).ToDateTime(new TimeOnly(6, 0));
        await ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Drawings', @s, @e, UTC_TIMESTAMP(3))",
            ("@u", busy.User.Id), ("@p", project.Id), ("@s", tuesday), ("@e", tuesday.AddHours(3)));

        await RunTwiceAsync(ScheduledCheck.WeeklySummary);

        var summary = (await OfKindAsync(busy, NotificationKinds.WeeklySummary)).ShouldHaveSingleItem();
        summary.Actor.ShouldBeNull();
        summary.Payload!.Value.GetProperty("weekStart").GetString().ShouldBe(lastWeek.ToString("yyyy-MM-dd"));
        summary.Payload!.Value.GetProperty("minutes").GetInt32().ShouldBe(180);
        summary.Payload!.Value.GetProperty("projects").GetInt32().ShouldBe(1);
        (await OfKindAsync(quiet, NotificationKinds.WeeklySummary)).ShouldHaveSingleItem()
            .Payload!.Value.GetProperty("minutes").GetInt32().ShouldBe(0);
        (await OfKindAsync(siteWorker, NotificationKinds.WeeklySummary)).ShouldBeEmpty();
        (await OfKindAsync(superAdmin, NotificationKinds.WeeklySummary)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_quiet_design_project_is_flagged_to_its_owner_once()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var quietProject = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var busyProject = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        await ExecuteAsync("UPDATE projects SET start_date = @d WHERE id IN (@a, @b)",
            ("@d", Today.AddDays(-10).ToDateTime(TimeOnly.MinValue)), ("@a", quietProject.Id), ("@b", busyProject.Id));
        await ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Sketches', DATE_SUB(UTC_TIMESTAMP(), INTERVAL 3 HOUR), DATE_SUB(UTC_TIMESTAMP(), INTERVAL 2 HOUR), UTC_TIMESTAMP(3))",
            ("@u", employee.User.Id), ("@p", busyProject.Id));

        await RunTwiceAsync(ScheduledCheck.IdleProjects);

        // Other tests' projects may be quiet too, so look at these two only.
        var flagged = await OfKindAsync(admin, NotificationKinds.ProjectIdle);
        var idle = flagged.Where(n => n.Project!.Id == quietProject.Id).ShouldHaveSingleItem();
        idle.Payload!.Value.GetProperty("days").GetInt32().ShouldBe(10);
        flagged.ShouldNotContain(n => n.Project!.Id == busyProject.Id);
        (await OfKindAsync(employee, NotificationKinds.ProjectIdle)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deadlines_warn_the_assignees_a_day_ahead_and_everyone_responsible_once_missed()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, assignee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        async Task<long> RequirementDueAsync(DateOnly deadline)
        {
            var response = await Client.PostJsonAsync($"/api/projects/{project.Id}/requirements",
                new { text = $"Due {deadline:yyyy-MM-dd}", assigneeIds = new[] { assignee.User.Id } }, admin.AccessToken);
            var requirement = await response.ReadAsync<RequirementResponse>();
            await ExecuteAsync("UPDATE project_requirements SET deadline = @d WHERE id = @id",
                ("@d", deadline.ToDateTime(TimeOnly.MinValue)), ("@id", requirement.Id));
            return requirement.Id;
        }

        var tomorrow = await RequirementDueAsync(Today.AddDays(1));
        var yesterday = await RequirementDueAsync(Today.AddDays(-1));
        await RequirementDueAsync(Today.AddDays(-10));

        await RunTwiceAsync(ScheduledCheck.RequirementDeadlines);

        (await OfKindAsync(assignee, NotificationKinds.RequirementDueSoon)).ShouldHaveSingleItem().SubjectId.ShouldBe(tomorrow);
        (await OfKindAsync(assignee, NotificationKinds.RequirementOverdue)).ShouldHaveSingleItem().SubjectId.ShouldBe(yesterday);
        (await OfKindAsync(admin, NotificationKinds.RequirementOverdue)).ShouldHaveSingleItem().SubjectId.ShouldBe(yesterday);
        (await OfKindAsync(admin, NotificationKinds.RequirementDueSoon)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_overdue_invoice_is_flagged_once_to_whoever_sees_the_money()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await Projects.SetUpBillingAsync(admin.AccessToken, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        var draft = await OkAsync<InvoiceResponse>(Client.PostJsonAsync($"/api/projects/{project.Id}/invoices",
            new { lines = new[] { new { description = "Concept design", amount = 900m } } }, admin.AccessToken));
        // Issued 60 days ago on 45-day terms: 15 days overdue.
        var invoice = await OkAsync<InvoiceResponse>(Client.PostJsonAsync($"/api/invoices/{draft.Id}/issue",
            new { issuedOn = Today.AddDays(-60) }, admin.AccessToken));

        await RunTwiceAsync(ScheduledCheck.OverdueInvoices);

        var overdue = (await OfKindAsync(superAdmin, NotificationKinds.InvoiceOverdue)).ShouldHaveSingleItem();
        overdue.SubjectId.ShouldBe(invoice.Id);
        overdue.Payload!.Value.GetProperty("number").GetString().ShouldBe(invoice.Number);
        overdue.Payload!.Value.TryGetProperty("amount", out _).ShouldBeFalse();
        (await OfKindAsync(admin, NotificationKinds.InvoiceOverdue)).ShouldHaveSingleItem();
        (await OfKindAsync(employee, NotificationKinds.InvoiceOverdue)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Whoever_sees_salaries_hears_once_who_finished_last_month_short()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await _steps.SetJoinedOnAsync(designer.User.Id, "2026-01-01");
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1760m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        await RunTwiceAsync(ScheduledCheck.ShortHours);

        // No work at all last month, so the designer is flagged.
        var month = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var alert = (await OfKindAsync(superAdmin, NotificationKinds.HoursShort)).ShouldHaveSingleItem();
        alert.Payload!.Value.GetProperty("month").GetString().ShouldBe(month.ToString("yyyy-MM"));
        alert.Payload!.Value.GetProperty("people").EnumerateArray()
            .ShouldContain(p => p.GetProperty("id").GetInt64() == designer.User.Id);
        // Salaries are company-level: a domain admin doesn't see them.
        (await OfKindAsync(admin, NotificationKinds.HoursShort)).ShouldBeEmpty();
    }
}
