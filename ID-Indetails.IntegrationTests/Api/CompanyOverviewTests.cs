using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Its own database: the overview adds up every project of the company.</summary>
public class CompanyOverviewTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

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

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    [Fact]
    public async Task Revenue_is_the_money_received_live_on_the_day_it_arrived()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);

        // 1,733.30 a month is 10 an hour; 6 hours in June (09:00-15:00 in Beirut) cost 60, plus 10 for the paid hour of break.
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1733.30m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 10_000m);
        await ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Drawings', '2026-06-10 06:00:00', '2026-06-10 12:00:00', UTC_TIMESTAMP(3))",
            ("@u", designer.User.Id), ("@p", project.Id));

        // A June expense and a June invoice in euros, which have no exchange rate: listed, never counted as 0. The API
        // only takes USD and LBP, so both are switched to euros in the database.
        var expense = await OkAsync<ExpenseResponse>(Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Print run", amount = 50m, currency = "USD", spentOn = "2026-06-15" }, superAdmin.AccessToken));
        await ExecuteAsync("UPDATE project_expenses SET currency = 'EUR' WHERE id = @id", ("@id", expense.Id));
        await new ProjectTestSteps(_steps).SetUpBillingAsync(superAdmin.AccessToken, DomainCodes.Design);
        var draft = await OkAsync<InvoiceResponse>(Client.PostJsonAsync($"/api/projects/{project.Id}/invoices",
            new { lines = new[] { new { description = "Stage 1", amount = 3000m } } }, superAdmin.AccessToken));
        (await Client.PostJsonAsync($"/api/invoices/{draft.Id}/issue", new { issuedOn = "2026-06-20" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        await ExecuteAsync("UPDATE invoices SET currency = 'EUR' WHERE id = @id", ("@id", draft.Id));

        // The project is still open: what counts is the money received, 4,000 in June and 6,000 in July.
        await OkAsync<object>(Client.PostJsonAsync($"/api/projects/{project.Id}/payments",
            new { amount = 4000m, currency = "USD", receivedOn = "2026-06-12" }, superAdmin.AccessToken));
        await OkAsync<object>(Client.PostJsonAsync($"/api/projects/{project.Id}/payments",
            new { amount = 6000m, currency = "USD", receivedOn = "2026-07-03" }, superAdmin.AccessToken));

        var june = await OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview?from=2026-06-01&to=2026-06-30", superAdmin.AccessToken));
        var design = june.Domains.Single(d => d.Domain == DomainCodes.Design);
        design.Revenue.ShouldBe(4000m);
        design.Collected.ShouldBe(4000m);
        design.Cost.ShouldBe(70m);
        design.Profit.ShouldBe(3930m);
        design.Margin.ShouldBe(0.9825m);
        design.ProjectsWithActivity.ShouldBe(1);
        design.Loggers.ShouldBe(1);
        june.Domains.Single(d => d.Domain == DomainCodes.Execution).Revenue.ShouldBe(0m);
        june.NetProfit.ShouldBe(3930m - june.Overhead);
        design.UnconvertedCosts.ShouldBe([new UnconvertedCostResponse(CostSource.Expenses, "EUR", 50m)]);
        design.Invoiced.ShouldBe(0m);
        design.UnconvertedInvoices.ShouldBe([new UnconvertedInvoicesResponse("EUR", 3000m, 1)]);
        june.UnconvertedInvoices.ShouldBe([new UnconvertedInvoicesResponse("EUR", 3000m, 1)]);

        // July had no work, but the rest of the payment arrived then
        var july = await OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview?from=2026-07-01&to=2026-07-31", superAdmin.AccessToken));
        july.Domains.Single(d => d.Domain == DomainCodes.Design).Revenue.ShouldBe(6000m);
        july.Domains.Single(d => d.Domain == DomainCodes.Design).Profit.ShouldBe(6000m);
        july.Domains.Single(d => d.Domain == DomainCodes.Design).UnconvertedCosts.ShouldBeEmpty();

        var months = await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?months=36", superAdmin.AccessToken));
        var juneMonth = months.Single(m => m.Month == new DateOnly(2026, 6, 1));
        juneMonth.Revenue.ShouldBe(4000m);
        juneMonth.Labour.ShouldBe(70m);
        juneMonth.ProfitBeforeOverhead.ShouldBe(3930m);
        months.Single(m => m.Month == new DateOnly(2026, 7, 1)).Revenue.ShouldBe(6000m);

        // a year's months, January first, the same figures
        var year = await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?year=2026", superAdmin.AccessToken));
        year[0].Month.ShouldBe(new DateOnly(2026, 1, 1));
        year.ShouldAllBe(m => m.Month.Year == 2026);
        year.Single(m => m.Month == new DateOnly(2026, 6, 1)).ShouldBe(juneMonth);
        var before = await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?year=2025", superAdmin.AccessToken));
        before.Select(m => m.Month).ShouldBe(Enumerable.Range(1, 12).Select(m => new DateOnly(2025, m, 1)));
        await (await Client.GetAsync($"/api/company/overview/monthly?year={DateTime.UtcNow.Year + 2}", superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);

        await (await Client.GetAsync("/api/company/overview", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Company_overhead_is_charged_to_the_period_by_calendar_days()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        (await Client.PostJsonAsync("/api/company/overhead",
            new { name = "Office rent", costGroup = "operating", currency = "USD", amount = 3000m }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var overview = await OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview", superAdmin.AccessToken));

        // This month to date: its elapsed share of 3,000 (at least 1 day's worth) is charged.
        overview.Overhead.ShouldBeGreaterThan(0m);
        overview.Overhead.ShouldBeLessThanOrEqualTo(3000m);
        overview.NetProfit.ShouldBe(overview.GrossProfit - overview.Overhead);

        var months = await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?months=1", superAdmin.AccessToken));
        months.ShouldHaveSingleItem().Overhead.ShouldBeGreaterThanOrEqualTo(3000m);
    }
}
