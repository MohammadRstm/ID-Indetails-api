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
    public async Task The_paid_break_is_labour_cost_shared_across_the_days_projects()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "hourly", amount = 20m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var morning = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 10_000m);
        var afternoon = await Projects.CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 10_000m);

        // Tuesday 10 February 2026 in Beirut (UTC+2): 09:00-13:00 on one project, 13:00-17:00 on the other.
        // The 9-to-5 day needs 7 hours, so the last hour is overtime and the in-hours split is 240 / 180 minutes;
        // the paid 60-minute break is shared the same way: 34.29 and 25.71 minutes.
        const string entrySql = "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Drawings', @start, @end, UTC_TIMESTAMP(3))";
        await ExecuteAsync(entrySql, ("@u", designer.User.Id), ("@p", morning.Id), ("@start", "2026-02-10 07:00:00"), ("@end", "2026-02-10 11:00:00"));
        await ExecuteAsync(entrySql, ("@u", designer.User.Id), ("@p", afternoon.Id), ("@start", "2026-02-10 11:00:00"), ("@end", "2026-02-10 15:00:00"));

        // 4 h x 20 = 80, plus 34.29 minutes of break at 20 an hour = 11.43.
        (await MoneyAsync(morning.Id, superAdmin.AccessToken)).Cost.Labour.ShouldBe(91.43m);
        (await MoneyAsync(afternoon.Id, superAdmin.AccessToken)).Cost.Labour.ShouldBe(88.57m);

        // A salaried person's break is paid too: 3,200 a month is the same 20 an hour in February (160 paid hours), so
        // the cost doesn't change.
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 3200m, currency = "USD", effectiveFrom = "2026-02-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await MoneyAsync(morning.Id, superAdmin.AccessToken)).Cost.Labour.ShouldBe(91.43m);
    }

    [Fact]
    public async Task Design_cost_is_labour_at_the_day_rate_plus_expenses_converted_to_usd()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, unpaid) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await EnsureLbpRateAsync(superAdmin.AccessToken);

        // 1,600 a month is 10 an hour in February 2026 (20 working days of 8 paid hours).
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1600m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var project = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Design, revenue: 10_000m);

        // 6 hours by the designer (08:00-14:00 in Beirut, so a working day with in-hours work: the paid hour of break
        // is cost too), and 30 minutes by a colleague with no pay record.
        const string entrySql = "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Drawings', '2026-02-10 06:00:00', @end, UTC_TIMESTAMP(3))";
        await ExecuteAsync(entrySql, ("@u", designer.User.Id), ("@p", project.Id), ("@end", "2026-02-10 12:00:00"));
        await ExecuteAsync(entrySql, ("@u", unpaid.User.Id), ("@p", project.Id), ("@end", "2026-02-10 06:30:00"));

        await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "Prints", amount = 100m, currency = "USD" }, admin.AccessToken);
        await Client.PostJsonAsync($"/api/projects/{project.Id}/expenses", new { label = "Taxi", amount = 895_000m, currency = "LBP" }, admin.AccessToken);

        var money = await MoneyAsync(project.Id, admin.AccessToken);

        money.Cost.Labour.ShouldBe(70m);
        money.Cost.Expenses.ShouldBe(110m);
        money.Cost.Total.ShouldBe(180m);
        money.Cost.UnpricedLabourMinutes.ShouldBe(30);
        money.Profit.ShouldBe(9820m);
        money.LabourHours.ShouldBe(6.5m);

        // No requirements done and under 10 hours: too early for a margin.
        money.Margin.ShouldBeNull();
        money.RecognisedToDate.ShouldBe(0m);
        money.CashProfit.ShouldBeNull();
    }

    [Fact]
    public async Task Execution_money_converts_site_expense_costs_by_the_day_spent_and_lists_the_ones_it_cannot()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Execution);
        await EnsureLbpRateAsync(superAdmin.AccessToken);

        // Fee 1,000.
        var site = await Projects.CreateProjectAsync(admin.AccessToken, DomainCodes.Execution);

        // an expense is dated by the day it was spent, whenever it was recorded
        const string expenseSql = "INSERT INTO site_expenses (project_id, name, amount, currency, spent_on, added_at, added_by_id) " +
            "VALUES (@p, @name, @amount, @currency, @on, UTC_TIMESTAMP(3), @u)";
        await ExecuteAsync(expenseSql, ("@p", site.Id), ("@name", "Cement"), ("@amount", 200m), ("@currency", "USD"), ("@on", "2026-03-02"), ("@u", admin.User.Id));
        await ExecuteAsync(expenseSql, ("@p", site.Id), ("@name", "Sand"), ("@amount", 900_000m), ("@currency", "LBP"), ("@on", "2026-03-02"), ("@u", admin.User.Id));

        // One labour day: 3 workers at 20.
        await ExecuteAsync("INSERT INTO labour_weeks (project_id, week_start, submitted_at, submitted_by_id) VALUES (@p, '2026-03-02', UTC_TIMESTAMP(3), @u)",
            ("@p", site.Id), ("@u", admin.User.Id));
        await ExecuteAsync("INSERT INTO labour_week_days (labour_week_id, work_date, workers, daily_pay) " +
            "SELECT id, '2026-03-03', 3, 20 FROM labour_weeks WHERE project_id = @p", ("@p", site.Id));

        (await Client.PostJsonAsync($"/api/projects/{site.Id}/payments", new { amount = 500m, currency = "USD" }, admin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        // The site expenses are plain costs: 200 + 900,000 LBP (10.06) and 60 of ground labour. Revenue is the work price alone.
        var money = await MoneyAsync(site.Id, admin.AccessToken);
        money.Revenue.ShouldBe(1000m);
        money.Cost.SiteExpenses.ShouldBe(210.06m);
        money.Cost.GroundLabour.ShouldBe(60m);
        money.Cost.Total.ShouldBe(270.06m);
        money.Profit.ShouldBe(729.94m);
        money.CashProfit.ShouldBe(229.94m);

        // An expense from a day before any LBP rate can't be converted: it is listed, not added.
        await ExecuteAsync(expenseSql, ("@p", site.Id), ("@name", "Old stock"), ("@amount", 179_000m), ("@currency", "LBP"), ("@on", "2025-06-01"), ("@u", admin.User.Id));
        var partial = await MoneyAsync(site.Id, admin.AccessToken);
        partial.Cost.Unconverted.ShouldHaveSingleItem().Source.ShouldBe(CostSource.SiteExpense);
        partial.Cost.Total.ShouldBe(270.06m);

        // The agreed amount is the work price plus supervision: it needs no exchange rate, so finalising is not held back.
        var completed = await Client.PostEmptyAsync($"/api/projects/{site.Id}/complete", admin.AccessToken);
        completed.IsSuccessStatusCode.ShouldBeTrue(await completed.Content.ReadAsStringAsync());
        (await MoneyAsync(site.Id, admin.AccessToken)).Revenue.ShouldBe(1000m);
    }
}
