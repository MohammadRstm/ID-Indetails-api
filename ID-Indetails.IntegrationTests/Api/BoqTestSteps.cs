using ID_Indetails.api.DTO.Response.Boqs;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Reusable steps for tests that need BOQs: made, priced and agreed through the real endpoints.</summary>
public sealed class BoqTestSteps(AuthTestSteps steps)
{
    private HttpClient Client => steps.Client;

    /// <summary>A line as the editor saves it.</summary>
    public static object Line(
        string description, decimal quantity, decimal rate, decimal waste = 0, string? code = null, string? unit = null, long? id = null)
        => new { id, code, description, quantity, wasteRate = waste, unit, rate };

    /// <summary>Starts a BOQ of <paramref name="kind"/> (internal, client or vo) and returns it as made.</summary>
    public async Task<BoqDetailResponse> CreateAsync(
        string accessToken, long projectId, string kind = "client", string? title = null, string? reason = null, long? fromBoqId = null)
    {
        var response = await Client.PostJsonAsync($"/api/projects/{projectId}/boqs", new { kind, title, reason, fromBoqId }, accessToken);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<BoqDetailResponse>();
    }

    /// <summary>Saves the whole document, with <paramref name="lines"/> from <see cref="Line"/>.</summary>
    public async Task<BoqDetailResponse> SaveAsync(
        string accessToken,
        long boqId,
        IEnumerable<object> lines,
        string title = "Villa Mounir",
        string adjustmentType = "none",
        decimal? adjustmentValue = null,
        string? reason = null,
        DateTime? updatedAt = null)
    {
        var response = await Client.PutJsonAsync($"/api/boqs/{boqId}", new { title, reason, adjustmentType, adjustmentValue, lines, updatedAt }, accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<BoqDetailResponse>();
    }

    public async Task<BoqDetailResponse> GetAsync(string accessToken, long boqId)
    {
        var response = await Client.GetAsync($"/api/boqs/{boqId}", accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<BoqDetailResponse>();
    }

    public async Task<BoqDetailResponse> AgreeAsync(string accessToken, long boqId)
    {
        var response = await Client.PostEmptyAsync($"/api/boqs/{boqId}/agree", accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<BoqDetailResponse>();
    }

    /// <summary>A client BOQ of one line worth <paramref name="work"/>, made and agreed: it sets the site's work price.</summary>
    public async Task<BoqDetailResponse> AgreeClientBoqAsync(string accessToken, long projectId, decimal work)
    {
        var boq = await CreateAsync(accessToken, projectId);
        await SaveAsync(accessToken, boq.Id, [Line("Works as agreed", 1, work, unit: "lot")]);
        return await AgreeAsync(accessToken, boq.Id);
    }

    /// <summary>A variation order of one line worth <paramref name="amount"/> (negative lowers the price), made and agreed.</summary>
    public async Task<BoqDetailResponse> AgreeVoAsync(string accessToken, long projectId, decimal amount, string reason = "The client asked for a change")
    {
        var vo = await CreateAsync(accessToken, projectId, "vo", reason: reason);
        await SaveAsync(accessToken, vo.Id, [Line("Change", 1, amount, unit: "lot")], "Variation order", reason: reason);
        return await AgreeAsync(accessToken, vo.Id);
    }
}
