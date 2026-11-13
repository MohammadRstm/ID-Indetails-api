using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Deleting a project made by mistake (docs/projects.md, "Deleting a project").</summary>
public class ProjectDeletionTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task ArchiveAsync(long projectId, string token)
    {
        var response = await Client.PostEmptyAsync($"/api/projects/{projectId}/archive", token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
    }

    private async Task<ProjectDeletionResponse> PreviewAsync(long projectId, string token)
    {
        var response = await Client.GetAsync($"/api/projects/{projectId}/deletion", token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ProjectDeletionResponse>();
    }

    private Task<HttpResponseMessage> DeleteAsync(long projectId, string? title, string token)
        => Client.DeleteJsonAsync($"/api/projects/{projectId}", new { title }, token);

    private Task<long> CountAsync(string sql, long id) => _steps.ScalarAsync<long>(sql, ("@id", id));

    [Fact]
    public async Task A_client_whose_project_was_deleted_can_then_be_deleted_and_the_projects_trace_stays()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var project = await Projects.CreateProjectAsync(token, DomainCodes.Design);
        var clientId = project.Client!.Id;
        await ArchiveAsync(project.Id, token);
        var deleted = await DeleteAsync(project.Id, project.Title, token);
        ((int)deleted.StatusCode).ShouldBe(204, await deleted.Content.ReadAsStringAsync());

        // the deleted project's trace names its client, which used to stop the client being deleted
        const string trace = "SELECT COUNT(*) FROM activity_events WHERE kind = 'project_deleted' AND subject_id = @id";
        (await CountAsync(trace + " AND client_id IS NOT NULL", project.Id)).ShouldBe(1);

        var gone = await Client.DeleteAsync($"/api/clients/{clientId}", token);
        ((int)gone.StatusCode).ShouldBe(204, await gone.Content.ReadAsStringAsync());
        (await CountAsync("SELECT COUNT(*) FROM clients WHERE id = @id", clientId)).ShouldBe(0);
        // the trace is kept, no longer pointing at the client
        (await CountAsync(trace + " AND client_id IS NULL", project.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_project_made_by_mistake_is_deleted_with_everything_on_it_once_archived_and_named()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var project = await Projects.CreateProjectAsync(token, DomainCodes.Design);
        var requirement = await Client.PostJsonAsync($"/api/projects/{project.Id}/requirements", new { text = "Survey" }, token);
        ((int)requirement.StatusCode).ShouldBe(201, await requirement.Content.ReadAsStringAsync());
        await ArchiveAsync(project.Id, token);

        // what goes with it, before anything is done
        var preview = await PreviewAsync(project.Id, token);
        preview.ShouldSatisfyAllConditions(
            p => p.CanDelete.ShouldBeTrue(),
            p => p.Blockers.ShouldBeEmpty(),
            p => p.Removes.ShouldContain("1 requirement"),
            p => p.Removes.ShouldContain(r => r.EndsWith(" activity entries")));

        // only its name, exactly, confirms it
        await (await DeleteAsync(project.Id, project.Title.ToUpperInvariant() + "x", token)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        (await CountAsync("SELECT COUNT(*) FROM projects WHERE id = @id", project.Id)).ShouldBe(1);

        var deleted = await DeleteAsync(project.Id, $"  {project.Title} ", token);
        ((int)deleted.StatusCode).ShouldBe(204, await deleted.Content.ReadAsStringAsync());

        (await Client.GetAsync($"/api/projects/{project.Id}", token)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        (await CountAsync("SELECT COUNT(*) FROM project_requirements WHERE project_id = @id", project.Id)).ShouldBe(0);
        (await CountAsync("SELECT COUNT(*) FROM activity_events WHERE project_id = @id", project.Id)).ShouldBe(0);
        // the one trace it leaves, and the client stays
        (await CountAsync(
            "SELECT COUNT(*) FROM activity_events WHERE kind = 'project_deleted' AND project_id IS NULL AND subject_id = @id", project.Id)).ShouldBe(1);
        (await Client.GetAsync($"/api/clients/{project.Client!.Id}", token)).StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_project_not_archived_or_that_holds_money_or_hours_is_never_deleted_and_says_why()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;

        var open = await Projects.CreateProjectAsync(token, DomainCodes.Design);
        (await PreviewAsync(open.Id, token)).Blockers.ShouldBe(["It isn't archived. Archive it first."]);
        await (await DeleteAsync(open.Id, open.Title, token)).ShouldFailWithAsync(422, ErrorCodes.ProjectNotDeletable);

        var paid = await Projects.CreateProjectAsync(token, DomainCodes.Design);
        var payment = await Client.PostJsonAsync($"/api/projects/{paid.Id}/payments", new { amount = 100m, currency = "USD" }, token);
        ((int)payment.StatusCode).ShouldBe(201, await payment.Content.ReadAsStringAsync());
        await ArchiveAsync(paid.Id, token);
        var refused = await PreviewAsync(paid.Id, token);
        refused.ShouldSatisfyAllConditions(p => p.CanDelete.ShouldBeFalse(), p => p.Blockers.ShouldBe(["1 payment received from the client."]));
        await (await DeleteAsync(paid.Id, paid.Title, token)).ShouldFailWithAsync(422, ErrorCodes.ProjectNotDeletable);
        (await CountAsync("SELECT COUNT(*) FROM projects WHERE id = @id", paid.Id)).ShouldBe(1);

        // hours from a closed month never change, so they keep the project for good
        var worked = await Projects.CreateProjectAsync(token, DomainCodes.Design);
        await new WorkEntryTestSteps(_steps).InsertAsync(superAdmin.User.Id, worked.Id, "2020-01-06 08:00:00", "2020-01-06 10:00:00");
        await ArchiveAsync(worked.Id, token);
        (await PreviewAsync(worked.Id, token)).Blockers.ShouldHaveSingleItem().ShouldContain("in months already closed");
        await (await DeleteAsync(worked.Id, worked.Title, token)).ShouldFailWithAsync(422, ErrorCodes.ProjectNotDeletable);
    }

    [Fact]
    public async Task Only_whoever_holds_project_delete_sees_or_deletes()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await ArchiveAsync(project.Id, superAdmin.AccessToken);

        await (await Client.GetAsync($"/api/projects/{project.Id}/deletion", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await DeleteAsync(project.Id, project.Title, admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        (await CountAsync("SELECT COUNT(*) FROM projects WHERE id = @id", project.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task The_lists_all_can_leave_archived_projects_out()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var project = await Projects.CreateProjectAsync(token, DomainCodes.Design);
        await ArchiveAsync(project.Id, token);

        async Task<IEnumerable<long>> IdsAsync(string query)
        {
            var response = await Client.GetAsync($"/api/projects?domain={DomainCodes.Design}&pageSize=100&{query}", token);
            ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
            return (await response.ReadAsync<PagedResponse<ProjectSummaryResponse>>()).Items.Select(p => p.Id);
        }

        (await IdsAsync("excludeArchived=true")).ShouldNotContain(project.Id);
        (await IdsAsync("excludeArchived=true&status=archived")).ShouldContain(project.Id);
        (await IdsAsync("sort=start")).ShouldContain(project.Id);
    }
}
