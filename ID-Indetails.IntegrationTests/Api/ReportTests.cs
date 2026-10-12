using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Reports;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ReportTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    [Fact]
    public async Task The_work_report_breaks_the_range_down_and_money_needs_financial_view()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, ben) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        // 40 an hour on a monthly salary in February 2026 (6,400 ÷ 160 paid hours).
        (await Client.PostJsonAsync($"/api/employees/{anna.User.Id}/pay",
            new { basis = "monthly", amount = 6400m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var brand = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var web = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        // Tuesday 10 February 2026 (Beirut is UTC+2): Anna 09:00-11:00 on brand and 13:00-14:00 on web, Ben 09:00-10:00.
        await Work.InsertAsync(anna.User.Id, brand.Id, "2026-02-10 07:00:00", "2026-02-10 09:00:00");
        await Work.InsertAsync(anna.User.Id, web.Id, "2026-02-10 11:00:00", "2026-02-10 12:00:00");
        await Work.InsertAsync(ben.User.Id, brand.Id, "2026-02-10 07:00:00", "2026-02-10 08:00:00");

        var from = new DateOnly(2026, 2, 9);
        var to = new DateOnly(2026, 2, 11);
        var report = await (await Client.GetAsync($"/api/reports/work?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", admin.AccessToken))
            .ReadAsync<WorkReportResponse>();

        // Other tests of this class log work too, so look at this test's projects and people only.
        var brandRow = report.ByProject.Single(p => p.ProjectId == brand.Id);
        brandRow.Minutes.ShouldBe(180);
        brandRow.People.ShouldBe(2);
        report.ByPerson.Single(p => p.UserId == anna.User.Id).Minutes.ShouldBe(180);
        report.Weekly.Count.ShouldBeGreaterThanOrEqualTo(3);
        report.Quarterly.Count.ShouldBe(6);
        report.Matrix.Rows.Single(r => r.UserId == ben.User.Id).TotalMinutes.ShouldBe(60);
        report.Money.ShouldNotBeNull();
        // 3 hours at 40, plus her paid hour of break at 40 (it was a working day with in-hours work).
        report.Money!.LabourByPerson.Single(p => p.UserId == anna.User.Id).LabourCost.Usd.ShouldBe(160m);
        report.Money.ProfitByProject.ShouldContain(p => p.ProjectId == brand.Id);

        var filtered = await (await Client.GetAsync($"/api/reports/work?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&userId={anna.User.Id}", admin.AccessToken))
            .ReadAsync<WorkReportResponse>();
        filtered.TotalMinutes.ShouldBe(180);
        filtered.PeopleCount.ShouldBe(1);

        // Without financial:view there is no money.
        (await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.FinancialView, action = "revoke", reason = "Test" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await (await Client.GetAsync("/api/reports/work", admin.AccessToken)).ReadAsync<WorkReportResponse>()).Money.ShouldBeNull();
    }

    /// <summary>
    /// Regression: the weekly bars reach back to whole weeks before a short range, but only the range's work was
    /// read, so those earlier bars showed 0 even when work was logged then.
    /// </summary>
    [Fact]
    public async Task Weekly_bars_before_a_short_range_show_the_work_logged_then_not_zero()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        // Tuesday 27 January 2026, two weeks before the range: 2 hours. Tuesday 10 February, in the range: 1 hour.
        await Work.InsertAsync(anna.User.Id, project.Id, "2026-01-27 07:00:00", "2026-01-27 09:00:00");
        await Work.InsertAsync(anna.User.Id, project.Id, "2026-02-10 07:00:00", "2026-02-10 08:00:00");

        var report = await (await Client.GetAsync($"/api/reports/work?from=2026-02-09&to=2026-02-11&userId={anna.User.Id}", admin.AccessToken))
            .ReadAsync<WorkReportResponse>();

        // the range's figures count the range only
        report.TotalMinutes.ShouldBe(60);
        report.ByProject.Single().Minutes.ShouldBe(60);
        // three weekly bars, oldest first: the first is the week of 27 January, with its real 2 hours
        report.Weekly.Count.ShouldBe(3);
        report.Weekly[0].Minutes.ShouldBe(120);
        report.Weekly[^1].Minutes.ShouldBe(60);
        report.ByProject.Single().Weekly.ShouldBe([120, 0, 60]);
    }

    [Fact]
    public async Task The_overview_compares_periods_and_flags_what_needs_attention()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, idle) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var paused = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        (await Client.PostEmptyAsync($"/api/projects/{paused.Id}/hold", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        var loggedAt = WorkEntryTestSteps.HoursAgo(3);
        await Work.LogAsync(employee.AccessToken, project.Id, loggedAt, 90);
        // in the first hours of a day, "3 hours ago" was yesterday, which may be last week or last quarter
        var beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
        var loggedToday = TimeZoneInfo.ConvertTimeFromUtc(loggedAt, beirut).Date == TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, beirut).Date;
        var expected = loggedToday ? 90 : 0;

        foreach (var period in new[] { "quarter", "month", "last30" })
        {
            var overview = await (await Client.GetAsync($"/api/reports/overview?period={period}", admin.AccessToken)).ReadAsync<OverviewResponse>();
            overview.PreviousTo.ShouldBeLessThan(overview.From);
            overview.Weekly.Count.ShouldBe(overview.PreviousWeekly.Count);
            overview.ProjectsByStatus.ShouldContain(s => s.Status == ProjectStatus.Hold);
        }

        var quarter = await (await Client.GetAsync("/api/reports/overview", admin.AccessToken)).ReadAsync<OverviewResponse>();
        quarter.Period.ShouldBe(OverviewPeriod.Quarter);
        quarter.Minutes.ShouldBeGreaterThanOrEqualTo(expected);
        quarter.PeopleLoggingThisWeek.ShouldBeGreaterThanOrEqualTo(loggedToday ? 1 : 0);
        quarter.PeopleCount.ShouldBeGreaterThanOrEqualTo(loggedToday ? 1 : 0);
        quarter.ProjectCount.ShouldBeGreaterThanOrEqualTo(loggedToday ? 1 : 0);
        quarter.ThisWeekMinutes.ShouldBeGreaterThanOrEqualTo(expected);
        quarter.Attention.Count.ShouldBeLessThanOrEqualTo(5);
        quarter.Attention.Single(a => a.Kind == AttentionKind.NotLogging).People!.ShouldContain(p => p.Id == idle.User.Id);

        await (await Client.GetAsync("/api/reports/overview?period=year", admin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        // days the admin picks, compared with as many days just before them
        var custom = await (await Client.GetAsync("/api/reports/overview?period=custom&from=2026-09-01&to=2026-09-10", admin.AccessToken))
            .ReadAsync<OverviewResponse>();
        custom.Period.ShouldBe(OverviewPeriod.Custom);
        (custom.From, custom.To).ShouldBe((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10)));
        (custom.PreviousFrom, custom.PreviousTo).ShouldBe((new DateOnly(2026, 8, 22), new DateOnly(2026, 8, 31)));
        await (await Client.GetAsync("/api/reports/overview?period=custom&from=2026-09-10", admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync("/api/reports/overview?period=custom&from=2026-09-10&to=2026-09-01", admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync("/api/reports/overview?period=month&from=2026-09-01", admin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Employees_and_other_domains_cannot_read_the_design_reports()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);

        await (await Client.GetAsync("/api/reports/work", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/reports/work", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync("/api/reports/overview", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
