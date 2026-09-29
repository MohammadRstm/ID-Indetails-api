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
    public async Task Execution_staff_see_their_own_requirements_and_never_the_other_domain_s()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, siteStaff) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var studio = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var pour = await AddAsync(site.Id, superAdmin.AccessToken, new { text = "Pour the slab", deadline = "2099-01-01", assigneeIds = new[] { siteStaff.User.Id } });
        await AddAsync(studio.Id, superAdmin.AccessToken, new { text = "Concept plans", assigneeIds = new[] { designer.User.Id } });

        var mine = await (await Client.GetAsync("/api/me/requirements", siteStaff.AccessToken)).ReadAsync<MyRequirementsResponse>();

        mine.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            r => r.Id.ShouldBe(pour.Id),
            r => r.Project.Id.ShouldBe(site.Id));
        (await (await Client.GetAsync("/api/me/requirements", designer.AccessToken)).ReadAsync<MyRequirementsResponse>())
            .Items.ShouldAllBe(r => r.Project.Id == studio.Id);
    }

    [Fact]
    public async Task Requirements_need_the_requirement_view_permission()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PutJsonAsync($"/api/users/{employee.User.Id}/permissions",
                new { changes = new[] { new { code = PermissionCodes.RequirementView, action = "revoke" } } }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        await (await Client.GetAsync("/api/me/requirements", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    private async Task<RequirementResponse> AddAsync(long projectId, string accessToken, object body)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/requirements", body, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<RequirementResponse>();
    }
}
