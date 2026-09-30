using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class MyWorkTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);
    private WorkEntryTestSteps Work => new(_steps);

    [Fact]
    public async Task The_summary_adds_up_the_callers_own_work()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var brand = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var web = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        var sketchesAt = WorkEntryTestSteps.HoursAgo(3);
        var bannerAt = WorkEntryTestSteps.HoursAgo(2);
        await Work.LogAsync(employee.AccessToken, brand.Id, sketchesAt, 60, "Logo sketches");
        await Work.LogAsync(employee.AccessToken, web.Id, bannerAt, 30, "Hero banner");
        await Work.LogAsync(colleague.AccessToken, web.Id, WorkEntryTestSteps.HoursAgo(3), 120, "Not mine");

        var summary = await (await Client.GetAsync("/api/me/work-summary", employee.AccessToken)).ReadAsync<WorkSummaryResponse>();
        summary.Person.Id.ShouldBe(employee.User.Id);
        summary.TotalMinutes.ShouldBe(90);
        summary.EntryCount.ShouldBe(2);
        summary.ProjectCount.ShouldBe(2);
        // in the first hours of a quarter, work from a few hours ago still belongs to the one before
        summary.ThisQuarterMinutes.ShouldBe((InThisQuarter(sketchesAt) ? 60 : 0) + (InThisQuarter(bannerAt) ? 30 : 0));
        summary.Weekly.Count.ShouldBe(16);
        summary.Weekly.Sum(w => w.Minutes).ShouldBe(90);
        summary.Week.Count.ShouldBe(7);
        summary.ActiveDaysLastYear.ShouldBeGreaterThanOrEqualTo(1);
        summary.ByProject.Select(p => p.ProjectId).ShouldBe([brand.Id, web.Id]);

        var searched = await (await Client.GetAsync("/api/me/work-summary?search=banner", employee.AccessToken)).ReadAsync<WorkSummaryResponse>();
        searched.ByProject.Single().ProjectId.ShouldBe(web.Id);
        searched.TotalMinutes.ShouldBe(90);
    }

    [Fact]
    public async Task My_projects_show_every_open_project_with_only_my_own_hours()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var worked = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var paused = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var done = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        (await Client.PostEmptyAsync($"/api/projects/{paused.Id}/hold", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PostEmptyAsync($"/api/projects/{done.Id}/complete", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await Work.LogAsync(employee.AccessToken, worked.Id, WorkEntryTestSteps.HoursAgo(4), 45, "Mine");
        await Work.LogAsync(colleague.AccessToken, worked.Id, WorkEntryTestSteps.HoursAgo(4), 200, "Theirs");

        var mine = await (await Client.GetAsync("/api/me/projects", employee.AccessToken)).ReadAsync<List<MyProjectResponse>>();
        mine.ShouldNotContain(p => p.Id == done.Id);
        mine[0].Id.ShouldBe(worked.Id);
        mine[0].MyMinutes.ShouldBe(45);
        mine[0].Contributors.Select(c => c.Id).ShouldBe([colleague.User.Id, employee.User.Id]);
        var onHold = mine.Single(p => p.Id == paused.Id);
        onHold.AcceptsWork.ShouldBeFalse();
        onHold.LastActivity.ShouldBeNull();

        var work = await (await Client.GetAsync($"/api/me/projects/{worked.Id}/work", employee.AccessToken)).ReadAsync<MyProjectWorkResponse>();
        work.MyMinutes.ShouldBe(45);
        work.TotalMinutes.ShouldBe(245);
        work.ContributorCount.ShouldBe(2);
        work.Recent.Single().Description.ShouldBe("Mine");
        work.Team.Single().Description.ShouldBe("Theirs");
        work.Weekly.Count.ShouldBe(8);
    }

    [Fact]
    public async Task Admins_read_a_persons_summary_but_not_across_domains()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await Work.SetDesignSettingsAsync(superAdmin.AccessToken);
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionEmployee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        await Work.LogAsync(employee.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(3), 60);

        (await (await Client.GetAsync($"/api/employees/{employee.User.Id}/work-summary", designAdmin.AccessToken)).ReadAsync<WorkSummaryResponse>())
            .TotalMinutes.ShouldBe(60);
        await (await Client.GetAsync($"/api/employees/{executionEmployee.User.Id}/work-summary", designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.GetAsync($"/api/employees/{designAdmin.User.Id}/work-summary", employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/me/projects", executionEmployee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/me/projects/{site.Id}/work", employee.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }

    /// <summary>Whether a moment falls in the company's current quarter (the company runs on Beirut time).</summary>
    private static bool InThisQuarter(DateTime utc)
    {
        var beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
        var at = TimeZoneInfo.ConvertTimeFromUtc(utc, beirut);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, beirut);
        return at.Year == now.Year && (at.Month - 1) / 3 == (now.Month - 1) / 3;
    }
}
