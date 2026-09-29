using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Requirements;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class MyRequirementTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    [Fact]
    public async Task Employees_see_only_their_own_open_requirements_soonest_first()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, me) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        var closed = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        var later = await AddAsync(project.Id, admin.AccessToken, new { text = "Elevations", deadline = "2099-01-01", assigneeIds = new[] { me.User.Id, colleague.User.Id } });
        var overdue = await AddAsync(project.Id, admin.AccessToken, new { text = "Concept plans", deadline = "2020-01-01", assigneeIds = new[] { me.User.Id } });
        var undated = await AddAsync(project.Id, admin.AccessToken, new { text = "Material board", assigneeIds = new[] { me.User.Id } });
        var done = await AddAsync(project.Id, admin.AccessToken, new { text = "Site survey", assigneeIds = new[] { me.User.Id } });
        await AddAsync(project.Id, admin.AccessToken, new { text = "Not mine", assigneeIds = new[] { colleague.User.Id } });
        await AddAsync(closed.Id, admin.AccessToken, new { text = "On a finished project", assigneeIds = new[] { me.User.Id } });
        await Client.PostEmptyAsync($"/api/projects/{project.Id}/requirements/{done.Id}/done", admin.AccessToken);
        await Client.PostEmptyAsync($"/api/projects/{closed.Id}/complete", admin.AccessToken);

        var mine = await (await Client.GetAsync("/api/me/requirements", me.AccessToken)).ReadAsync<MyRequirementsResponse>();

        mine.Items.Select(r => r.Id).ShouldBe([overdue.Id, later.Id, undated.Id]);
        mine.Items[0].ShouldSatisfyAllConditions(
            r => r.IsOverdue.ShouldBeTrue(),
            r => r.DaysLeft!.Value.ShouldBeLessThan(0),
            r => r.Project.Id.ShouldBe(project.Id));
        mine.Items[1].OtherAssignees.Select(a => a.Id).ShouldBe([colleague.User.Id]);
        mine.Items[2].DaysLeft.ShouldBeNull();
        mine.ShouldSatisfyAllConditions(
            m => m.Projects.ShouldBe(1),
            m => m.Overdue.ShouldBe(1),
            m => m.DueSoon.ShouldBe(0),
            m => m.Done.ShouldBe(1));

        (await (await Client.GetAsync("/api/me/requirements", colleague.AccessToken)).ReadAsync<MyRequirementsResponse>())
            .Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Execution_staff_do_not_reach_the_design_self_service_views()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, siteStaff) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);

        await (await Client.GetAsync("/api/me/requirements", siteStaff.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    private async Task<RequirementResponse> AddAsync(long projectId, string accessToken, object body)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/requirements", body, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<RequirementResponse>();
    }
}
