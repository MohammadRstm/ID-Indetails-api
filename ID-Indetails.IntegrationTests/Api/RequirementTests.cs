using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Requirements;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class RequirementTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<RequirementResponse> AddAsync(long projectId, string token, object body)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/requirements", body, token);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<RequirementResponse>();
    }

    private async Task<int> ProgressAsync(long projectId, string token)
        => (await (await Client.GetAsync($"/api/projects/{projectId}", token)).ReadAsync<ProjectResponse>()).ProgressPct;

    [Fact]
    public async Task Admin_adds_assigns_ticks_and_removes_requirements_and_progress_follows()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        var first = await AddAsync(project.Id, admin.AccessToken, new { text = "Concept plans", deadline = "2020-01-01", assigneeIds = new[] { employee.User.Id } });
        first.IsOverdue.ShouldBeTrue();
        first.Assignees.Single().Id.ShouldBe(employee.User.Id);
        var second = await AddAsync(project.Id, admin.AccessToken, new { text = "Elevations" });
        var third = await AddAsync(project.Id, admin.AccessToken, new { text = "Renders" });

        var ticked = await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/requirements/{first.Id}/done", admin.AccessToken))
            .ReadAsync<RequirementResponse>();
        ticked.IsDone.ShouldBeTrue();
        ticked.IsOverdue.ShouldBeFalse();
        ticked.DoneBy!.Id.ShouldBe(admin.User.Id);
        (await ProgressAsync(project.Id, admin.AccessToken)).ShouldBe(33);

        // Ticking again changes nothing.
        (await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/requirements/{first.Id}/done", admin.AccessToken))
            .ReadAsync<RequirementResponse>()).DoneAt.ShouldBe(ticked.DoneAt);

        var edited = await (await Client.PutJsonAsync($"/api/projects/{project.Id}/requirements/{second.Id}",
            new { text = "Elevations, rev B", assigneeIds = new[] { employee.User.Id } }, admin.AccessToken)).ReadAsync<RequirementResponse>();
        edited.Text.ShouldBe("Elevations, rev B");
        edited.Assignees.Count.ShouldBe(1);

        (await Client.DeleteAsync($"/api/projects/{project.Id}/requirements/{third.Id}", admin.AccessToken)).StatusCode
            .ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await ProgressAsync(project.Id, admin.AccessToken)).ShouldBe(50);

        await Client.PostEmptyAsync($"/api/projects/{project.Id}/requirements/{first.Id}/undone", admin.AccessToken);
        var list = await (await Client.GetAsync($"/api/projects/{project.Id}/requirements", employee.AccessToken))
            .ReadAsync<RequirementListResponse>();
        list.Total.ShouldBe(2);
        list.Done.ShouldBe(0);
        list.Overdue.ShouldBe(1);
        list.ProgressPct.ShouldBe(0);
        list.Items.Select(r => r.Id).ShouldBe([first.Id, second.Id]);

        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/requirements/{third.Id}", new { text = "Gone" }, admin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND subject_type = 'requirement'", ("@p", project.Id))).ShouldBe(7);
    }

    [Fact]
    public async Task Only_employees_of_the_domain_can_be_assigned()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, outsider) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        foreach (var id in new[] { admin.User.Id, outsider.User.Id, 999_999 })
        {
            await (await Client.PostJsonAsync($"/api/projects/{project.Id}/requirements", new { text = "X", assigneeIds = new[] { id } }, admin.AccessToken))
                .ShouldFailWithAsync(422, ErrorCodes.AssigneeNotAllowed);
        }
    }

    [Fact]
    public async Task Finished_projects_are_read_only_until_reopened_and_auto_ticks_are_undone()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var manual = await AddAsync(project.Id, superAdmin.AccessToken, new { text = "Survey" });
        var open = await AddAsync(project.Id, superAdmin.AccessToken, new { text = "Handover" });
        await Client.PostEmptyAsync($"/api/projects/{project.Id}/requirements/{manual.Id}/done", superAdmin.AccessToken);

        await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken);
        var closed = await (await Client.GetAsync($"/api/projects/{project.Id}/requirements", superAdmin.AccessToken))
            .ReadAsync<RequirementListResponse>();
        closed.Items.Single(r => r.Id == open.Id).AutoDone.ShouldBeTrue();
        closed.ProgressPct.ShouldBe(100);

        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/requirements", new { text = "Late" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectClosed);
        await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/requirements/{open.Id}/undone", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectClosed);

        await Client.PostEmptyAsync($"/api/projects/{project.Id}/reopen", superAdmin.AccessToken);
        var reopened = await (await Client.GetAsync($"/api/projects/{project.Id}/requirements", superAdmin.AccessToken))
            .ReadAsync<RequirementListResponse>();
        reopened.Items.Single(r => r.Id == open.Id).IsDone.ShouldBeFalse();
        reopened.Items.Single(r => r.Id == manual.Id).IsDone.ShouldBeTrue();
        reopened.ProgressPct.ShouldBe(50);
    }

    [Fact]
    public async Task Employees_only_read_and_other_domains_read_as_not_found()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var requirement = await AddAsync(project.Id, superAdmin.AccessToken, new { text = "Plans", assigneeIds = new[] { employee.User.Id } });

        (await Client.GetAsync($"/api/projects/{project.Id}/requirements", employee.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/requirements/{requirement.Id}/done", employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/requirements", new { text = "X" }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);

        await (await Client.GetAsync($"/api/projects/{project.Id}/requirements", executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/requirements", new { text = "X" }, executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.DeleteAsync($"/api/projects/{project.Id}/requirements/{requirement.Id}", executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
