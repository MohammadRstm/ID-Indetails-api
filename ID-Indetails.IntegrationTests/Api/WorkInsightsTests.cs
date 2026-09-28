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
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, anna) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, ben) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, lead) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{anna.User.Id}/pay",
            new { basis = "hourly", amount = 30m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await Client.PutJsonAsync($"/api/users/{lead.User.Id}/permissions",
            new { changes = new[] { new { code = PermissionCodes.WorkEntryViewAll, action = "grant", reason = "Team lead" } } },
            superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await Work.LogAsync(anna.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(6), 120);
        await Work.LogAsync(anna.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(3), 60);
        await Work.LogAsync(ben.AccessToken, project.Id, WorkEntryTestSteps.HoursAgo(6), 60);

        var work = await (await Client.GetAsync($"/api/projects/{project.Id}/work", admin.AccessToken)).ReadAsync<ProjectWorkResponse>();
        work.TotalMinutes.ShouldBe(240);
        work.EntryCount.ShouldBe(3);
        work.ContributorCount.ShouldBe(2);
        work.ActivityLevel.ShouldBe(ActivityLevel.Quiet);
        work.MinutesLast14Days.ShouldBe(240);
        work.Weekly.Count.ShouldBeGreaterThanOrEqualTo(4);
        work.HourHeat.Sum(c => c.Minutes).ShouldBe(240);
        var first = work.Contributors[0];
        first.UserId.ShouldBe(anna.User.Id);
        first.SharePct.ShouldBe(75);
        first.Weekly.Sum().ShouldBe(180);
        first.LabourCost!.Usd.ShouldBe(90m);
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
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
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
}
