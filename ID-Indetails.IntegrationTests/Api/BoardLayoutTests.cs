using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.BoardLayouts;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class BoardLayoutTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<BoardLayoutResponse> GetAsync(string key, string token)
    {
        var response = await Client.GetAsync($"/api/me/board-layouts/{key}", token);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<BoardLayoutResponse>();
    }

    [Fact]
    public async Task Each_person_saves_replaces_and_resets_their_own_board_order()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var board = $"/api/me/board-layouts/{BoardKeys.DesignOverview}";

        var nothing = await GetAsync(BoardKeys.DesignOverview, admin.AccessToken);
        nothing.Order.ShouldBeNull();
        nothing.SavedAt.ShouldBeNull();

        var saved = await (await Client.PutJsonAsync(board, new { order = new[] { "money", "figures", "hours" } }, admin.AccessToken))
            .ReadAsync<BoardLayoutResponse>();
        saved.Order.ShouldBe(["money", "figures", "hours"]);
        (await GetAsync(BoardKeys.DesignOverview, admin.AccessToken)).Order.ShouldBe(["money", "figures", "hours"]);

        // Saving again replaces it; someone else's board is their own.
        (await Client.PutJsonAsync(board, new { order = new[] { "figures", "money" } }, admin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await GetAsync(BoardKeys.DesignOverview, admin.AccessToken)).Order.ShouldBe(["figures", "money"]);
        (await GetAsync(BoardKeys.DesignOverview, superAdmin.AccessToken)).Order.ShouldBeNull();
        (await GetAsync(BoardKeys.ExecutionOverview, admin.AccessToken)).Order.ShouldBeNull();

        ((int)(await Client.DeleteAsync(board, admin.AccessToken)).StatusCode).ShouldBe(204);
        (await GetAsync(BoardKeys.DesignOverview, admin.AccessToken)).Order.ShouldBeNull();
        ((int)(await Client.DeleteAsync(board, admin.AccessToken)).StatusCode).ShouldBe(204);
    }

    [Fact]
    public async Task Unknown_boards_and_bad_orders_are_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var board = $"/api/me/board-layouts/{BoardKeys.CompanyOverview}";

        await (await Client.GetAsync("/api/me/board-layouts/somewhere-else", superAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PutJsonAsync("/api/me/board-layouts/somewhere-else", new { order = new[] { "keeps" } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);

        foreach (var order in new string[][]
        {
            [],
            ["keeps", "keeps"],
            ["Keeps"],
            Enumerable.Range(0, 31).Select(i => $"card-{i}").ToArray(),
        })
        {
            await (await Client.PutJsonAsync(board, new { order }, superAdmin.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        }

        await (await Client.GetAsync(board)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
    }
}
