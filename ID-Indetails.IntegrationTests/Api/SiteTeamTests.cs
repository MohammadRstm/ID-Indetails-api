using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class SiteTeamTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task A_site_s_team_is_whoever_recorded_its_materials_latest_first()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, siteAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        var (_, _, rami) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, layla) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var projects = new ProjectTestSteps(_steps);
        var site = await projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var design = await projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        foreach (var person in new[] { rami, layla })
        {
            var added = await Client.PostJsonAsync($"/api/projects/{site.Id}/materials", new { items = new[] { new { name = "Tiles", amount = 100m } } }, person.AccessToken);
            ((int)added.StatusCode).ShouldBe(201, await added.Content.ReadAsStringAsync());
        }

        await _steps.ExecuteAsync("UPDATE project_materials SET added_at = DATE_SUB(added_at, INTERVAL 1 DAY) WHERE added_by_id = @id", ("@id", rami.User.Id));

        var detail = await (await Client.GetAsync($"/api/projects/{site.Id}", siteAdmin.AccessToken)).ReadAsync<ProjectResponse>();
        detail.Team!.Count.ShouldBe(2);
        detail.Team.People.Select(p => p.Id).ShouldBe([layla.User.Id, rami.User.Id]);

        var row = (await (await Client.GetAsync("/api/projects?domain=execution", siteAdmin.AccessToken))
            .ReadAsync<PagedResponse<ProjectSummaryResponse>>()).Items.Single(p => p.Id == site.Id);
        row.Team!.Count.ShouldBe(2);

        (await (await Client.GetAsync($"/api/projects/{design.Id}", superAdmin.AccessToken)).ReadAsync<ProjectResponse>()).Team.ShouldBeNull();
    }
}
