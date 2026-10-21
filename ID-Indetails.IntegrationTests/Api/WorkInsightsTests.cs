using ID_Indetails.api.Enums;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class WorkInsightsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    [Fact]
    public async Task A_projects_work_shows_contributors_and_cost_only_to_money_viewers()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, ben) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, lead) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        // 30 an hour on a monthly salary in February 2026 (4,800 ÷ 160 paid hours).
        (await Client.PostJsonAsync($"/api/employees/{anna.User.Id}/pay",
            new { basis = "monthly", amount = 4800m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PutJsonAsync($"/api/users/{lead.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.WorkEntryViewAll, action = "grant", reason = "Team lead" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        // Anna's paid work at fixed times (Tuesday 10 February 2026, 09:00-11:00 and 13:00-14:00 in Beirut), so her cost
        // doesn't depend on when the test runs; Ben, who has no pay record, logs recent work for the activity figures.
        await Work.InsertAsync(anna.User.Id, project.Id, "2026-02-10 07:00:00", "2026-02-10 09:00:00");
        await Work.InsertAsync(anna.User.Id, project.Id, "2026-02-10 11:00:00", "2026-02-10 12:00:00");
        await Work.LogAsync(ben.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(6), 60);

        var work = await (await Client.GetAsync($"/api/projects/{project.Id}/work", admin.AccessToken)).ReadAsync<ProjectWorkResponse>();
        work.TotalMinutes.ShouldBe(240);
        work.EntryCount.ShouldBe(3);
        work.ContributorCount.ShouldBe(2);
        work.ActivityLevel.ShouldBe(ActivityLevel.Quiet);
        work.MinutesLast14Days.ShouldBe(60);
        work.Weekly.Count.ShouldBeGreaterThanOrEqualTo(4);
        work.HourHeat.Sum(c => c.Minutes).ShouldBe(240);
        var first = work.Contributors[0];
        first.UserId.ShouldBe(anna.User.Id);
        first.SharePct.ShouldBe(75);
        // 3 hours at 30, plus her paid hour of break at 30.
        first.LabourCost!.Usd.ShouldBe(120m);
        work.Contributors[1].Weekly.Sum().ShouldBe(60);
        work.Contributors[1].LabourCost!.UnpricedMinutes.ShouldBe(60);

        var forLead = await (await Client.GetAsync($"/api/projects/{project.Id}/work", lead.AccessToken)).ReadAsync<ProjectWorkResponse>();
        forLead.Contributors.ShouldAllBe(c => c.LabourCost == null);

        var drawer = await (await Client.GetAsync($"/api/projects/{project.Id}/work/people/{anna.User.Id}", admin.AccessToken))
            .ReadAsync<ProjectContributorDetailResponse>();
        drawer.Minutes.ShouldBe(180);
        drawer.Entries.ShouldBe(2);
        drawer.RecentEntries.Count.ShouldBe(2);
        await (await Client.GetAsync($"/api/projects/{project.Id}/work/people/{lead.User.Id}", admin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task The_people_list_has_everyone_of_design_with_their_hours()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionEmployee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await Work.LogAsync(employee.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(3), 90);

        var people = await (await Client.GetAsync("/api/work/people", admin.AccessToken)).ReadAsync<List<PersonWorkResponse>>();
        people.ShouldNotContain(p => p.Id == executionEmployee.User.Id);
        people.ShouldNotContain(p => p.Id == superAdmin.User.Id);
        var row = people.Single(p => p.Id == employee.User.Id);
        row.TotalMinutes.ShouldBe(90);
        row.Entries.ShouldBe(1);
        row.QuarterMinutes.ShouldBe(90);
        row.Focus!.ProjectId.ShouldBe(project.Id);
        row.Weekly.Count.ShouldBe(8);
        row.LabourCost!.UnpricedMinutes.ShouldBe(90);

        var searched = await (await Client.GetAsync($"/api/work/people?search={Uri.EscapeDataString(employee.User.Email)}", admin.AccessToken))
            .ReadAsync<List<PersonWorkResponse>>();
        searched.Single().Id.ShouldBe(employee.User.Id);
    }

    [Fact]
    public async Task People_rows_carry_the_last_30_days_this_month_s_overtime_and_the_rate_for_money_viewers()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        // Paid by the hour, so today's rate is the same whatever month the test runs in.
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "hourly", amount = 10m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        // An hour today from 18:00 in Beirut, after hours whatever the weekday: all of it is overtime.
        var beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, beirut));
        var evening = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(new TimeOnly(18, 0)), beirut);
        await Work.InsertAsync(designer.User.Id, project.Id, evening.ToString("yyyy-MM-dd HH:mm:ss"), evening.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss"));

        var row = (await (await Client.GetAsync("/api/work/people", admin.AccessToken)).ReadAsync<List<PersonWorkResponse>>())
            .Single(p => p.Id == designer.User.Id);
        row.MinutesLast30Days.ShouldBe(60);
        row.OvertimeMinutesThisMonth.ShouldBe(60);
        row.HourlyRate.ShouldBe(new CurrencyAmountResponse("USD", 10m));

        // Without financial:view the rate is left out.
        (await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
                new { changes = new[] { new { code = PermissionCodes.FinancialView, action = "revoke", reason = "Hours only" } } }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await (await Client.GetAsync("/api/work/people", admin.AccessToken)).ReadAsync<List<PersonWorkResponse>>())
            .Single(p => p.Id == designer.User.Id).HourlyRate.ShouldBeNull();
    }

    [Fact]
    public async Task Employees_and_other_domains_cannot_reach_the_insights()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        await (await Client.GetAsync($"/api/projects/{project.Id}/work", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/work/people", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/{project.Id}/work", executionAdmin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/{site.Id}/work", superAdmin.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.NotForThisDomain);
    }

    [Fact]
    public async Task The_timeline_shows_each_day_with_people_meetings_and_status_changes()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken, holdLogging: true);
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        // on one company day: the timeline counts work by day, so 50 minutes across midnight would be two days
        var entry = await Work.LogAsync(employee.AccessToken, project.Id, WorkEntryTestSteps.HoursAgoOnOneDay(3, 50), 50);
        (await Client.PostEmptyAsync($"/api/projects/{project.Id}/hold", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var timeline = await (await Client.GetAsync($"/api/projects/{project.Id}/timeline", admin.AccessToken)).ReadAsync<ProjectTimelineResponse>();
        var day = timeline.Days.Single(d => d.Minutes > 0);
        day.Minutes.ShouldBe(50);
        day.People.Single().UserId.ShouldBe(employee.User.Id);
        timeline.Events.Select(e => e.Kind).ShouldContain(ActivityEventKinds.ProjectHold);
        timeline.Events.First().Kind.ShouldBe(ActivityEventKinds.ProjectCreated);

        await (await Client.GetAsync($"/api/projects/{project.Id}/timeline", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
    }
}
