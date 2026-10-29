using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Finance;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Expected revenue on the domain summary: what was agreed, on its day, apart from the money received.</summary>
public class ExpectedRevenueTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task<T> OkAsync<T>(string url, string token)
    {
        var response = await Client.GetAsync(url, token);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    [Fact]
    public async Task Expected_revenue_counts_what_was_agreed_on_its_day_apart_from_what_was_received()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        async Task<decimal> ExpectedAsync(string domain) => (await OkAsync<FinanceSummaryResponse>($"/api/finance/{domain}/summary", token)).ExpectedRevenue;

        // Design: a client project's payment on the day it is agreed, and a change by its difference
        var design = await ExpectedAsync(DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(token, DomainCodes.Design, revenue: 5000m);
        (await ExpectedAsync(DomainCodes.Design)).ShouldBe(design + 5000m);
        (await Client.PutJsonAsync($"/api/projects/{project.Id}/payment", new { revenue = 4000m, description = "Smaller scope" }, token))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await ExpectedAsync(DomainCodes.Design)).ShouldBe(design + 4000m);
        // nothing received yet, so it isn't revenue
        var summary = await OkAsync<FinanceSummaryResponse>("/api/finance/design/summary", token);
        (await OkAsync<List<FinanceMonthResponse>>("/api/finance/design/monthly?months=1", token)).Single().ExpectedRevenue.ShouldBe(summary.ExpectedRevenue);

        // Execution: the work price on the day a BOQ is agreed, and a variation order by its difference; a material is a cost
        var execution = await ExpectedAsync(DomainCodes.Execution);
        var site = await Projects.CreateProjectAsync(token, DomainCodes.Execution);
        (await ExpectedAsync(DomainCodes.Execution)).ShouldBe(execution + 1000m);
        (await Client.PostJsonAsync($"/api/projects/{site.Id}/materials", new { items = new[] { new { name = "Cement", amount = 100m } } }, token))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await ExpectedAsync(DomainCodes.Execution)).ShouldBe(execution + 1000m);

        var agreed = await Projects.CreateProjectAsync(token, DomainCodes.Execution, projectFee: 0m);
        (await ExpectedAsync(DomainCodes.Execution)).ShouldBe(execution + 1000m);
        await Projects.AgreeWorkPriceAsync(token, agreed.Id, 2000m);
        (await ExpectedAsync(DomainCodes.Execution)).ShouldBe(execution + 3000m);
        await new BoqTestSteps(_steps).AgreeVoAsync(token, agreed.Id, -500m);
        (await ExpectedAsync(DomainCodes.Execution)).ShouldBe(execution + 2500m);
    }
}
