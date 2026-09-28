using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Teams;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class TeamTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private static string NewName() => $"Team {Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Everyone_sees_the_seeded_teams_of_their_own_domains()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);

        var mine = await (await Client.GetAsync("/api/teams", employee.AccessToken)).ReadAsync<List<TeamResponse>>();
        mine.ShouldAllBe(t => t.Domain == DomainCodes.Execution);
        mine.Select(t => t.Name).ShouldContain("Site Operations");

        var all = await (await Client.GetAsync("/api/teams", superAdmin.AccessToken)).ReadAsync<List<TeamResponse>>();
        all.Select(t => t.Domain).Distinct().ShouldBe(DomainCodes.All, ignoreOrder: true);
    }

    [Fact]
    public async Task Admin_creates_renames_and_deletes_a_team_in_their_domain()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var name = NewName();

        var created = await Client.PostJsonAsync("/api/teams", new { name }, designAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var team = await created.ReadAsync<TeamResponse>();
        team.Domain.ShouldBe(DomainCodes.Design);

        await (await Client.PostJsonAsync("/api/teams", new { name = name.ToUpperInvariant() }, designAdmin.AccessToken))
            .ShouldFailWithAsync(409, ErrorCodes.TeamNameTaken);

        var renamed = await (await Client.PutJsonAsync($"/api/teams/{team.Id}", new { name = name + "x" }, designAdmin.AccessToken))
            .ReadAsync<TeamResponse>();
        renamed.Name.ShouldBe(name + "x");

        (await Client.DeleteAsync($"/api/teams/{team.Id}", designAdmin.AccessToken)).StatusCode
            .ShouldBe(System.Net.HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_team_with_people_cannot_be_deleted()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var team = await (await Client.PostJsonAsync("/api/teams", new { name = NewName(), domainCode = DomainCodes.Design }, superAdmin.AccessToken))
            .ReadAsync<TeamResponse>();

        (await Client.PutJsonAsync($"/api/employees/{employee.User.Id}", new { firstName = "Test", lastName = "Member", teamId = team.Id }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        await (await Client.DeleteAsync($"/api/teams/{team.Id}", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.TeamNotEmpty);
    }

    [Fact]
    public async Task Teams_of_another_domain_are_out_of_reach_and_employees_cannot_manage_teams()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designAdmin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        await (await Client.PostJsonAsync("/api/teams", new { name = NewName(), domainCode = DomainCodes.Execution }, designAdmin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PutJsonAsync("/api/teams/7", new { name = NewName() }, designAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync("/api/teams", new { name = NewName() }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync("/api/teams", new { name = NewName() }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }
}
