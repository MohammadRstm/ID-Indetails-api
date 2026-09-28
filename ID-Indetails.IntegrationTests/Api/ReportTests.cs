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
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, ben) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{anna.User.Id}/pay",
            new { basis = "hourly", amount = 40m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var brand = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var web = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await Work.LogAsync(anna.AccessToken, brand.Id, WorkEntryTestSteps.HoursAgo(6), 120);
        await Work.LogAsync(anna.AccessToken, web.Id, WorkEntryTestSteps.HoursAgo(3), 60);
        await Work.LogAsync(ben.AccessToken, brand.Id, WorkEntryTestSteps.HoursAgo(6), 60);

        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3);
        var to = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
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
        report.Money!.LabourByPerson.Single(p => p.UserId == anna.User.Id).LabourCost.Usd.ShouldBe(120m);
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

    [Fact]
    public async Task The_overview_compares_periods_and_flags_what_needs_attention()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var paused = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        (await Client.PostEmptyAsync($"/api/projects/{paused.Id}/hold", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await Work.LogAsync(employee.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(3), 90);

        foreach (var period in new[] { "quarter", "month", "last30" })
        {
            var overview = await (await Client.GetAsync($"/api/reports/overview?period={period}", admin.AccessToken)).ReadAsync<OverviewResponse>();
            overview.PreviousTo.ShouldBeLessThan(overview.From);
            overview.Weekly.Count.ShouldBe(overview.PreviousWeekly.Count);
            overview.ProjectsByStatus.ShouldContain(s => s.Status == ProjectStatus.Hold);
        }

        var quarter = await (await Client.GetAsync("/api/reports/overview", admin.AccessToken)).ReadAsync<OverviewResponse>();
        quarter.Period.ShouldBe(OverviewPeriod.Quarter);
        quarter.Minutes.ShouldBeGreaterThanOrEqualTo(90);
        quarter.PeopleLoggingThisWeek.ShouldBeGreaterThanOrEqualTo(1);
        quarter.Attention.Count.ShouldBeLessThanOrEqualTo(5);

        await (await Client.GetAsync("/api/reports/overview?period=year", admin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
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
