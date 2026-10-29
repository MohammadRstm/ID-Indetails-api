using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Materials;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Materials sent twice (an answer lost on the way to the phone) are added once.</summary>
public class MaterialClientRefTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private Task<HttpResponseMessage> SendAsync(long projectId, string token, params object[] items)
        => Client.PostJsonAsync($"/api/projects/{projectId}/materials", new { items }, token);

    private async Task<List<MaterialResponse>> AddAsync(long projectId, string token, params object[] items)
    {
        var response = await SendAsync(projectId, token, items);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<List<MaterialResponse>>();
    }

    private Task<int> CountAsync(string sql, long projectId) => _steps.ScalarAsync<int>(sql, ("@p", projectId));

    [Fact]
    public async Task A_batch_sent_again_returns_the_materials_already_added_without_adding_events_or_notifications()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, worker) = await _steps.MaterialWorkerAsync(superAdmin);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var cement = Guid.NewGuid();
        var sand = Guid.NewGuid();

        var first = await AddAsync(site.Id, worker.AccessToken,
            new { name = "Cement", amount = 120m, clientRef = cement }, new { name = "Sand", amount = 40m, clientRef = sand });
        var again = await AddAsync(site.Id, worker.AccessToken,
            new { name = "Cement", amount = 120m, clientRef = cement }, new { name = "Sand", amount = 40m, clientRef = sand });

        again.Select(m => m.Id).ShouldBe(first.Select(m => m.Id));
        (await CountAsync("SELECT COUNT(*) FROM project_materials WHERE project_id = @p", site.Id)).ShouldBe(2);
        (await CountAsync("SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'material_added'", site.Id)).ShouldBe(2);
        (await CountAsync("SELECT COUNT(*) FROM notifications WHERE project_id = @p AND kind = 'materials_added'", site.Id))
            .ShouldBe(await CountAsync("SELECT COUNT(DISTINCT recipient_id) FROM notifications WHERE project_id = @p AND kind = 'materials_added'", site.Id));

        // part new, part sent before: only the new one is added, in request order
        var gravel = Guid.NewGuid();
        var mixed = await AddAsync(site.Id, worker.AccessToken,
            new { name = "Gravel", amount = 60m, clientRef = gravel }, new { name = "Cement", amount = 120m, clientRef = cement }, new { name = "Nails", amount = 5m });
        mixed[1].Id.ShouldBe(first[0].Id);
        mixed[0].Name.ShouldBe("Gravel");
        mixed[2].Name.ShouldBe("Nails");
        (await CountAsync("SELECT COUNT(*) FROM project_materials WHERE project_id = @p", site.Id)).ShouldBe(4);
        (await CountAsync("SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'material_added'", site.Id)).ShouldBe(4);
    }

    [Fact]
    public async Task Client_refs_are_each_person_s_own_and_one_material_s_only()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, worker) = await _steps.MaterialWorkerAsync(superAdmin);
        var (_, _, colleague) = await _steps.MaterialWorkerAsync(superAdmin);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var otherSite = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var shared = Guid.NewGuid();

        var mine = await AddAsync(site.Id, worker.AccessToken, new { name = "Cement", amount = 120m, clientRef = shared });
        var theirs = await AddAsync(site.Id, colleague.AccessToken, new { name = "Cement", amount = 120m, clientRef = shared });
        theirs[0].Id.ShouldNotBe(mine[0].Id);

        await (await SendAsync(otherSite.Id, worker.AccessToken, new { name = "Cement", amount = 120m, clientRef = shared }))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        var twice = Guid.NewGuid();
        await (await SendAsync(site.Id, worker.AccessToken,
                new { name = "Sand", amount = 1m, clientRef = twice }, new { name = "Sand", amount = 1m, clientRef = twice }))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task A_finalised_site_still_answers_a_batch_it_already_has_but_takes_nothing_new()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, worker) = await _steps.MaterialWorkerAsync(superAdmin);
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var cement = Guid.NewGuid();
        var first = await AddAsync(site.Id, worker.AccessToken, new { name = "Cement", amount = 120m, clientRef = cement });

        var completed = await Client.PostEmptyAsync($"/api/projects/{site.Id}/complete", superAdmin.AccessToken);
        completed.IsSuccessStatusCode.ShouldBeTrue(await completed.Content.ReadAsStringAsync());

        var again = await AddAsync(site.Id, worker.AccessToken, new { name = "Cement", amount = 120m, clientRef = cement });
        again.ShouldHaveSingleItem().Id.ShouldBe(first[0].Id);
        again[0].CanEdit.ShouldBeFalse();
        await (await SendAsync(site.Id, worker.AccessToken, new { name = "Sand", amount = 40m, clientRef = Guid.NewGuid() }))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectLocked);
    }
}
