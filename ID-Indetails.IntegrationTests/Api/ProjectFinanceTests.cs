using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Its own database: exchange rates are company-wide and can only be added in date order.</summary>
public class ProjectFinanceTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private ProjectTestSteps Projects => new(_steps);

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await api.Database.OpenConnectionAsync();
        await using var command = new MySqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<ProjectMoneyResponse> MoneyAsync(long projectId, string token)
        => (await (await Client.GetAsync($"/api/projects/{projectId}", token)).ReadAsync<ProjectResponse>()).Money!;

    private async Task EnsureLbpRateAsync(string token)
    {
        // 89,500 LBP to the dollar from 1 January 2026; a repeat is refused, which is fine here.
        await Client.PostJsonAsync("/api/exchange-rates", new { currency = "LBP", ratePerUsd = 89_500m, effectiveFrom = "2026-01-01" }, token);
    }

    [Fact]
    public async Task Design_cost_is_labour_at_the_day_rate_plus_expenses_converted_to_usd()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await EnsureLbpRateAsync(superAdmin.AccessToken);

        // 1,733.30 a month is 10 an hour (÷ 173.33).
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1733.30m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 10_000m);

        // 6 hours by the designer, and 30 minutes by the admin, who has no pay record.
        const string entrySql = "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Drawings', '2026-02-10 06:00:00', @end, UTC_TIMESTAMP(3))";
        await ExecuteAsync(entrySql, ("@u", designer.User.Id), ("@p", project.Id), ("@end", "2026-02-10 12:00:00"));
        await ExecuteAsync(entrySql, ("@u", admin.User.Id), ("@p", project.Id), ("@end", "2026-02-10 06:30:00"));

        await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "Prints", amount = 100m, currency = "USD" }, admin.AccessToken);
        await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "Taxi", amount = 895_000m, currency = "LBP" }, admin.AccessToken);

        var money = await MoneyAsync(project.Id, admin.AccessToken);

        money.Cost.Labour.ShouldBe(60m);
        money.Cost.Expenses.ShouldBe(110m);
        money.Cost.Total.ShouldBe(170m);
        money.Cost.UnpricedLabourMinutes.ShouldBe(30);
        money.Profit.ShouldBe(9830m);
        money.LabourHours.ShouldBe(6.5m);

        // No requirements done and under 10 hours: too early for a margin.
        money.Margin.ShouldBeNull();
        money.RecognisedToDate.ShouldBe(0m);
        money.CashProfit.ShouldBeNull();
    }

    [Fact]
    public async Task Execution_money_converts_materials_and_refuses_to_finalise_with_unconvertible_ones()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Execution);
        await EnsureLbpRateAsync(superAdmin.AccessToken);

        // Fee 1,000 and a 10% markup.
        var site = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);

        const string materialSql = "INSERT INTO project_materials (project_id, name, amount, currency, added_at, added_by_id) " +
            "VALUES (@p, @name, @amount, @currency, @at, @u)";
        await ExecuteAsync(materialSql, ("@p", site.Id), ("@name", "Cement"), ("@amount", 200m), ("@currency", "USD"), ("@at", "2026-03-02 08:00:00"), ("@u", admin.User.Id));
        await ExecuteAsync(materialSql, ("@p", site.Id), ("@name", "Sand"), ("@amount", 900_000m), ("@currency", "LBP"), ("@at", "2026-03-02 08:00:00"), ("@u", admin.User.Id));

        // One labour day: 3 workers at 20.
        await ExecuteAsync("INSERT INTO labour_weeks (project_id, week_start, submitted_at, submitted_by_id) VALUES (@p, '2026-03-02', UTC_TIMESTAMP(3), @u)",
            ("@p", site.Id), ("@u", admin.User.Id));
        await ExecuteAsync("INSERT INTO labour_week_days (labour_week_id, work_date, workers, daily_pay) " +
            "SELECT id, '2026-03-03', 3, 20 FROM labour_weeks WHERE project_id = @p", ("@p", site.Id));

        (await Client.PostJsonAsync($"/api/projects/{site.Id}/payments", new { amount = 500m, currency = "USD" }, admin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var money = await MoneyAsync(site.Id, admin.AccessToken);

        // Charge: 220 + 990,000 LBP (11.06). Cost: 200 + 900,000 LBP (10.06) + 60 ground labour.
        money.MaterialCharge.ShouldBe(231.06m);
        money.Revenue.ShouldBe(1231.06m);
        money.Cost.Materials.ShouldBe(210.06m);
        money.Cost.GroundLabour.ShouldBe(60m);
        money.Cost.Total.ShouldBe(270.06m);
        money.Profit.ShouldBe(961m);
        money.CashProfit.ShouldBe(229.94m);
        money.MaterialProfit.ShouldBe(21m);
        money.UnconvertedMaterials.ShouldBe(0);

        // A material from a day before any LBP rate can't be converted: it is listed, not added.
        await ExecuteAsync(materialSql, ("@p", site.Id), ("@name", "Old stock"), ("@amount", 179_000m), ("@currency", "LBP"), ("@at", "2025-06-01 08:00:00"), ("@u", admin.User.Id));
        var partial = await MoneyAsync(site.Id, admin.AccessToken);
        partial.UnconvertedMaterials.ShouldBe(1);
        partial.Cost.Unconverted.ShouldHaveSingleItem().Source.ShouldBe(CostSource.Materials);
        partial.Cost.Total.ShouldBe(270.06m);

        // The agreed amount is frozen on finalising, so it must not leave a material out.
        await (await Client.PostEmptyAsync($"/api/projects/{site.Id}/complete", admin.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.ExchangeRateMissing);
    }
}
