using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ProjectStatusTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<ProjectResponse> ChangeAsync(long projectId, string action, string token)
    {
        var response = await Client.PostEmptyAsync($"/api/projects/{projectId}/{action}", token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectResponse>();
    }

    /// <summary>Requirements come through their own endpoints in the requirements phase; here they are seeded directly.</summary>
    private async Task AddRequirementsAsync(long projectId, long userId, int open, int done)
    {
        await using var connection = await api.Database.OpenConnectionAsync();
        for (var i = 0; i < open + done; i++)
        {
            await using var insert = new MySqlCommand(
                "INSERT INTO project_requirements (project_id, text, is_done, done_at, auto_done, created_at, created_by_id) " +
                "VALUES (@p, 'Deliverable', @d, IF(@d, UTC_TIMESTAMP(), NULL), 0, UTC_TIMESTAMP(), @u)", connection);
            insert.Parameters.AddWithValue("@p", projectId);
            insert.Parameters.AddWithValue("@d", i >= open);
            insert.Parameters.AddWithValue("@u", userId);
            await insert.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Completing_finalises_and_reopening_undoes_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);
        await AddRequirementsAsync(project.Id, admin.User.Id, open: 3, done: 1);

        (await ChangeAsync(project.Id, "hold", admin.AccessToken)).Status.ShouldBe(ProjectStatus.Hold);
        var completed = await ChangeAsync(project.Id, "complete", admin.AccessToken);
        completed.Status.ShouldBe(ProjectStatus.Completed);
        completed.ProgressPct.ShouldBe(100);
        completed.CompletedAt.ShouldNotBeNull();
        completed.Money!.IsLocked.ShouldBeTrue();
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM project_requirements WHERE project_id = @p AND auto_done = 1", ("@p", project.Id)))
            .ShouldBe(3);

        await (await Client.PutJsonAsync($"/api/projects/{project.Id}/billing", new { billingType = "client", clientId = project.Client!.Id, revenue = 1 }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
        await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.StatusUnchanged);
        await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/hold", admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.InvalidStatusChange);

        var reopened = await ChangeAsync(project.Id, "reopen", admin.AccessToken);
        reopened.Status.ShouldBe(ProjectStatus.Active);
        reopened.ProgressPct.ShouldBe(25);
        reopened.CompletedAt.ShouldBeNull();
        reopened.Money!.IsLocked.ShouldBeFalse();

        var kinds = await _steps.ScalarAsync<string>(
            "SELECT GROUP_CONCAT(kind ORDER BY id) FROM activity_events WHERE project_id = @p AND kind <> 'project_created' AND kind <> 'revenue_set'",
            ("@p", project.Id));
        // The seeded requirements bypassed the API, so the first change also brings the cached progress up to 25%.
        kinds.ShouldBe("project_hold,progress_set,project_completed,project_finalized,progress_set,project_reopened,progress_set");
    }

    [Fact]
    public async Task Archiving_unfinished_work_finalises_it_and_projects_without_requirements_read_as_done()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        var archived = await ChangeAsync(project.Id, "archive", superAdmin.AccessToken);
        archived.Status.ShouldBe(ProjectStatus.Archived);
        archived.FinalizedAt.ShouldNotBeNull();
        archived.ProgressPct.ShouldBe(100);

        (await ChangeAsync(project.Id, "reopen", superAdmin.AccessToken)).ProgressPct.ShouldBe(0);

        var completed = await ChangeAsync(project.Id, "complete", superAdmin.AccessToken);
        var archivedAfter = await ChangeAsync(project.Id, "archive", superAdmin.AccessToken);
        archivedAfter.FinalizedAt.ShouldBe(completed.FinalizedAt);
    }

    [Fact]
    public async Task Finalising_an_execution_site_freezes_its_agreed_amount()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);

        var completed = await ChangeAsync(site.Id, "complete", admin.AccessToken);
        completed.Money!.Revenue.ShouldBe(1000m);
        (await _steps.ScalarAsync<decimal>("SELECT revenue FROM projects WHERE id = @p", ("@p", site.Id))).ShouldBe(1000m);
        await (await Client.PutJsonAsync($"/api/projects/{site.Id}/pricing", new { projectFee = 2000m, materialMarkup = 0.1m }, admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);

        await ChangeAsync(site.Id, "reopen", admin.AccessToken);
        (await _steps.ScalarAsync<decimal>("SELECT revenue FROM projects WHERE id = @p", ("@p", site.Id))).ShouldBe(0m);
        var repriced = await (await Client.PutJsonAsync($"/api/projects/{site.Id}/pricing", new { projectFee = 2000m, materialMarkup = 0.1m }, admin.AccessToken))
            .ReadAsync<ProjectResponse>();
        repriced.Money!.Revenue.ShouldBe(2000m);
    }

    [Fact]
    public async Task Only_status_managers_of_the_domain_change_status()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostEmptyAsync("/api/projects/999999/complete", superAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
