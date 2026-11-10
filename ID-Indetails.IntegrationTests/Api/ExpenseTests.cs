using System.Net.Http.Headers;
using System.Text;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Projects;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

public class ExpenseTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    /// <summary>
    /// 89,500 LBP to the dollar from 1 January 2026, the class's only LBP rate. Rates are company-wide and added in date
    /// order: whichever test asks second is refused, which is fine.
    /// </summary>
    private Task LbpRateAsync(string token)
        => Client.PostJsonAsync("/api/exchange-rates", new { currency = "LBP", ratePerUsd = 89_500m, effectiveFrom = "2026-01-01" }, token);

    [Fact]
    public async Task A_value_that_cannot_be_read_is_named_in_words_never_in_the_readers_own()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var url = $"/api/projects/{project.Id}/expenses";

        // too large for any number the API reads into an amount (a decimal stops near 7.9e28)
        var huge = await Client.PostJsonAsync(url, new { label = "Taxi", amount = 1e40, currency = "USD" }, superAdmin.AccessToken);
        var body = await huge.Content.ReadAsStringAsync();
        var error = await huge.ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        error.Message.ShouldBe("Amount is too large.");
        error.Errors.ShouldNotBeNull().Keys.ShouldBe(["$.amount"]);
        error.Errors["$.amount"].ShouldBe(["Amount is too large."]);
        body.ShouldNotContain("System.");
        body.ShouldNotContain("LineNumber");

        // readable, but more than an amount column holds: the validator says so
        (await (await Client.PostJsonAsync(url, new { label = "Taxi", amount = 100_000_000_000_000_000_000m, currency = "USD" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Errors!["amount"].ShouldBe(["Amount is too large."]);

        // a word where a number goes
        (await (await Client.PostJsonAsync(url, new { label = "Taxi", amount = "lots", currency = "USD" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed)).Message.ShouldBe("Amount isn't valid.");

        // not JSON at all
        var broken = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent("{\"label\": ", Encoding.UTF8, "application/json") };
        broken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var unreadable = await Client.SendAsync(broken);
        var text = await unreadable.Content.ReadAsStringAsync();
        await unreadable.ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        text.ShouldNotContain("System.");
        text.ShouldNotContain("LineNumber");
    }

    [Fact]
    public async Task Admin_adds_and_removes_expenses_with_totals_per_currency()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        var created = await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Render farm", amount = 120.50m, currency = "usd", category = "software", spentOn = "2026-09-01" }, admin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        var licence = await created.ReadAsync<ExpenseResponse>();
        licence.Currency.ShouldBe("USD");

        // an LBP expense needs a rate in force on its day, today here
        await LbpRateAsync(superAdmin.AccessToken);
        await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "Taxi", amount = 900_000m, currency = "LBP" }, admin.AccessToken);
        var printing = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Printing", amount = 30m, currency = "USD" }, admin.AccessToken)).ReadAsync<ExpenseResponse>();
        printing.Category.ShouldBe("other");

        (await Client.DeleteAsync($"/api/projects/{project.Id}/expenses/{printing.Id}", admin.AccessToken)).StatusCode
            .ShouldBe(System.Net.HttpStatusCode.NoContent);

        var list = await (await Client.GetAsync($"/api/projects/{project.Id}/expenses", admin.AccessToken)).ReadAsync<ExpenseListResponse>();
        list.Items.Count.ShouldBe(2);
        list.Totals.ShouldBe([new CurrencyTotalResponse("LBP", 900_000m), new CurrencyTotalResponse("USD", 120.50m)]);

        var money = (await (await Client.GetAsync($"/api/projects/{project.Id}", admin.AccessToken)).ReadAsync<ProjectResponse>()).Money!;
        money.ExpenseTotals.Count.ShouldBe(2);

        await (await Client.DeleteAsync($"/api/projects/{project.Id}/expenses/{printing.Id}", admin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
        (await _steps.ScalarAsync<int>(
            "SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND subject_type = 'expense'", ("@p", project.Id))).ShouldBe(4);
    }

    /// <summary>
    /// An expense in another currency counts only with an exchange rate in force on its day, as a site expense does: without
    /// one the project's money would leave it out, so it is refused instead. The class's LBP rate starts on 1 January 2026,
    /// so 31 December 2025 never has one, whichever test added the rate.
    /// </summary>
    [Fact]
    public async Task An_expense_in_another_currency_needs_a_rate_in_force_on_its_day()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var url = $"/api/projects/{project.Id}/expenses";

        var refused = await (await Client.PostJsonAsync(url,
                new { label = "Permit stamps", amount = 4_475_000m, currency = "LBP", spentOn = "2025-12-31" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ExchangeRateMissing);
        refused.Message.ShouldContain("There is no LBP exchange rate in force on 2025-12-31");
        (await (await Client.GetAsync(url, superAdmin.AccessToken)).ReadAsync<ExpenseListResponse>()).Items.ShouldBeEmpty();
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'expense_added'", ("@p", project.Id)))
            .ShouldBe(0);

        // the same day in USD needs no rate
        var courier = await Client.PostJsonAsync(url, new { label = "Courier", amount = 50m, currency = "USD", spentOn = "2025-12-31" }, superAdmin.AccessToken);
        ((int)courier.StatusCode).ShouldBe(201, await courier.Content.ReadAsStringAsync());

        // from the rate's first day the LBP expense is recorded and converted (4,475,000 at 89,500 is 50); the day before
        // still has no rate
        await LbpRateAsync(superAdmin.AccessToken);
        var recorded = await Client.PostJsonAsync(url,
            new { label = "Permit stamps", amount = 4_475_000m, currency = "LBP", spentOn = "2026-01-01" }, superAdmin.AccessToken);
        ((int)recorded.StatusCode).ShouldBe(201, await recorded.Content.ReadAsStringAsync());
        await (await Client.PostJsonAsync(url,
                new { label = "Permit stamps", amount = 4_475_000m, currency = "LBP", spentOn = "2025-12-31" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ExchangeRateMissing);

        var list = await (await Client.GetAsync(url, superAdmin.AccessToken)).ReadAsync<ExpenseListResponse>();
        list.Items.Select(e => (e.Label, e.Currency, e.SpentOn))
            .ShouldBe([("Permit stamps", "LBP", new DateOnly(2026, 1, 1)), ("Courier", "USD", new DateOnly(2025, 12, 31))], ignoreOrder: true);
        (await (await Client.GetAsync($"/api/projects/{project.Id}", superAdmin.AccessToken)).ReadAsync<ProjectResponse>()).Money!.Cost.Expenses
            .ShouldBe(100m);
    }

    [Fact]
    public async Task An_execution_site_takes_no_project_expense_its_costs_are_site_expenses()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var site = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Execution);
        var design = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        await (await Client.PostJsonAsync($"/api/projects/{site.Id}/expenses", new { label = "Crane", amount = 90m, currency = "USD" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ExpenseExecutionUsesSiteExpenses);
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM project_expenses WHERE project_id = @p", ("@p", site.Id))).ShouldBe(0);
        (await _steps.ScalarAsync<int>("SELECT COUNT(*) FROM activity_events WHERE project_id = @p AND kind = 'expense_added'", ("@p", site.Id))).ShouldBe(0);

        // a Design project's expenses are as they were: the categories are still the project ones
        var created = await Client.PostJsonAsync($"/api/projects/{design.Id}/expenses",
            new { label = "Render farm", amount = 40m, currency = "USD", category = "software" }, superAdmin.AccessToken);
        ((int)created.StatusCode).ShouldBe(201, await created.Content.ReadAsStringAsync());
        (await created.ReadAsync<ExpenseResponse>()).Category.ShouldBe("software");
        var list = await (await Client.GetAsync($"/api/projects/{design.Id}/expenses", superAdmin.AccessToken)).ReadAsync<ExpenseListResponse>();
        list.Items.ShouldHaveSingleItem().Label.ShouldBe("Render farm");
        list.Totals.ShouldHaveSingleItem().Total.ShouldBe(40m);
    }

    [Fact]
    public async Task Finished_projects_take_no_expense_changes_until_reopened()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var expense = await (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Travel", amount = 50m, currency = "USD" }, superAdmin.AccessToken)).ReadAsync<ExpenseResponse>();

        await Client.PostEmptyAsync($"/api/projects/{project.Id}/complete", superAdmin.AccessToken);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "Late", amount = 5m, currency = "USD" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectClosed);
        await (await Client.DeleteAsync($"/api/projects/{project.Id}/expenses/{expense.Id}", superAdmin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ProjectClosed);

        await Client.PostEmptyAsync($"/api/projects/{project.Id}/reopen", superAdmin.AccessToken);
        (await Client.DeleteAsync($"/api/projects/{project.Id}/expenses/{expense.Id}", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Employees_and_other_domains_cannot_reach_expenses()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, employee) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, executionAdmin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        var project = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        await (await Client.GetAsync($"/api/projects/{project.Id}/expenses", employee.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "X", amount = 1m, currency = "USD" }, employee.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync($"/api/projects/{project.Id}/expenses", executionAdmin.AccessToken)).ShouldFailWithAsync(404, ErrorCodes.NotFound);
        await (await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "X", amount = 1m, currency = "USD" }, executionAdmin.AccessToken))
            .ShouldFailWithAsync(404, ErrorCodes.NotFound);
    }
}
