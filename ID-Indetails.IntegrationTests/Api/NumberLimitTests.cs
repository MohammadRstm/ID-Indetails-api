using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Labour;
using ID_Indetails.api.Validators.Common;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Numbers too large to keep are refused in words: each field has its ceiling, and so do the sums, products and
/// conversions the API stores from them. Anything still too large for its column is a 400, never a server error.
/// </summary>
public class NumberLimitTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private static async Task<string> RefusedAsync(HttpResponseMessage response, string field)
        => (await response.ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors.ShouldNotBeNull()[field].ShouldHaveSingleItem();

    [Fact]
    public async Task An_invoice_whose_lines_add_up_to_too_much_is_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var line = new { description = "Stage", amount = MoneyRules.MaxAmount };

        (await RefusedAsync(await Client.PostJsonAsync($"/api/projects/{project.Id}/invoices", new { lines = new[] { line, line } }, superAdmin.AccessToken), "lines"))
            .ShouldBe("The invoice's total is too large.");
    }

    [Fact]
    public async Task A_labour_day_whose_cost_is_too_large_is_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var weekStart = (await (await Client.GetAsync($"/api/projects/{site.Id}/labour-weeks", superAdmin.AccessToken)).ReadAsync<LabourWeekListResponse>()).Weeks[0].WeekStart;
        var day = (await (await Client.GetAsync($"/api/projects/{site.Id}/labour-weeks/{weekStart:yyyy-MM-dd}", superAdmin.AccessToken)).ReadAsync<LabourWeekResponse>()).Days[^1].Date;

        (await RefusedAsync(await Client.PutJsonAsync($"/api/projects/{site.Id}/labour-weeks/{weekStart:yyyy-MM-dd}",
                new { days = new[] { new { date = day, workers = 10_000, dailyPay = 10_000_000_000_000m } } }, superAdmin.AccessToken), "days[0].dailyPay"))
            .ShouldBe("Workers × daily pay is too large.");
    }

    [Fact]
    public async Task A_material_whose_quantity_and_unit_cost_give_too_large_a_cost_is_refused()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);

        (await RefusedAsync(await Client.PostJsonAsync($"/api/projects/{site.Id}/materials",
                new { items = new[] { new { name = "Marble", quantity = 99_999_999_999m, unitCost = 1_000_000m } } }, superAdmin.AccessToken), "items[0].unitCost"))
            .ShouldBe("Quantity × unit cost is too large.");
        (await RefusedAsync(await Client.PostJsonAsync($"/api/projects/{site.Id}/materials",
                new { items = new[] { new { name = "Marble", quantity = 100_000_000_000m, amount = 10m } } }, superAdmin.AccessToken), "items[0].quantity"))
            .ShouldBe("Quantity is too large.");
    }

    [Fact]
    public async Task A_payment_rate_or_its_dollar_value_too_large_is_refused_and_one_left_too_large_to_save_is_said_in_words()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var url = $"/api/projects/{project.Id}/payments";

        (await RefusedAsync(await Client.PostJsonAsync(url, new { amount = 100m, currency = "LBP", fxRate = 2_000_000_000_000m }, superAdmin.AccessToken), "fxRate"))
            .ShouldBe("Exchange rate is too large.");
        (await RefusedAsync(await Client.PostJsonAsync(url, new { amount = 1_000_000_000_000m, currency = "LBP", fxRate = 0.000001m }, superAdmin.AccessToken), "amount"))
            .ShouldBe("At this exchange rate, the amount in US dollars is too large.");

        // with the day's rate instead of one typed in, the dollar value only shows once worked out: too large for its
        // column, it is the client's error, in words
        (await Client.PostJsonAsync("/api/exchange-rates", new { currency = "LBP", ratePerUsd = 0.000001m }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var saved = await Client.PostJsonAsync(url, new { amount = 1_000_000_000_000m, currency = "LBP" }, superAdmin.AccessToken);
        var body = await saved.Content.ReadAsStringAsync();
        (await saved.ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Message.ShouldBe("A number here is too large to be saved.");
        body.ShouldNotContain("Out of range");
    }
}
