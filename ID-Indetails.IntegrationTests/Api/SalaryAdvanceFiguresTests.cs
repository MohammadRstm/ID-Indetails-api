using System.Globalization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Enums;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// What a salary advance changes in Salaries and the company overview: when cash leaves, never what anyone earns. Only a
/// write-off is a cost. Everyone is paid 1,760 USD a month from January 2026 on a fixed salary.
/// </summary>
public class SalaryAdvanceFiguresTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private static DateOnly CompanyToday()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut")));

    private static string Cents(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

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

    /// <summary>Execution site staff on 1,760 a month since 1 January 2026: a fixed salary, the same every month.</summary>
    private async Task<AuthResponse> SalariedAsync(AuthResponse superAdmin)
    {
        var (_, _, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        await _steps.SetJoinedOnAsync(person.User.Id, "2026-01-01");
        (await Client.PostJsonAsync($"/api/employees/{person.User.Id}/pay",
            new { basis = "monthly", amount = 1760m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        return person;
    }

    private async Task<SalariesResponse> ListAsync(AuthResponse superAdmin, AuthResponse person, string month)
        => await OkAsync<SalariesResponse>(Client.GetAsync($"/api/company/salaries?month={month}&search={person.User.Email}", superAdmin.AccessToken));

    private async Task<SalaryAdvanceResponse> RecordAsync(AuthResponse superAdmin, AuthResponse person, decimal amount, string month)
        => await OkAsync<SalaryAdvanceResponse>(Client.PostJsonAsync("/api/company/salary-advances",
            new { userId = person.User.Id, amount, months = new[] { new { month, amount } }, note = "Test advance" }, superAdmin.AccessToken));

    [Fact]
    public async Task An_advance_comes_off_what_is_handed_over_while_the_month_runs_and_changes_no_pay()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var person = await SalariedAsync(superAdmin);
        var thisMonth = new DateOnly(CompanyToday().Year, CompanyToday().Month, 1);
        var month = thisMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var before = (await ListAsync(superAdmin, person, month)).Rows.Items.ShouldHaveSingleItem();
        before.AdvanceRepayment.ShouldBeNull();
        before.FullMonthEstimate.ShouldBe(1760m);
        before.ToHandOver.ShouldBe(1760m);

        await RecordAsync(superAdmin, person, 400m, thisMonth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var list = await ListAsync(superAdmin, person, month);
        var row = list.Rows.Items.ShouldHaveSingleItem();
        row.AdvanceRepayment.ShouldBe(400m);
        row.ToHandOver.ShouldBe(1360m);
        // what the person earns, and what it costs, is the same
        row.Total.ShouldBe(before.Total);
        row.BasePay.ShouldBe(before.BasePay);
        row.HourlyRate.ShouldBe(before.HourlyRate);
        row.MonthlyPay.ShouldBe(before.MonthlyPay);
        row.FullMonthEstimate.ShouldBe(1760m);

        var total = list.Totals.ShouldHaveSingleItem();
        total.AdvanceRepayment.ShouldBe(400m);
        total.ToHandOver.ShouldBe(1360m);
        total.Total.ShouldBe(row.Total);
        list.Advances.ShouldNotBeNull();

        var detail = await OkAsync<SalaryDetailResponse>(Client.GetAsync($"/api/company/salaries/{person.User.Id}?month={month}", superAdmin.AccessToken));
        detail.Salary.AdvanceRepayment.ShouldBe(400m);
        detail.Salary.ToHandOver.ShouldBe(1360m);
        var advance = detail.Advances.ShouldHaveSingleItem();
        advance.Amount.ShouldBe(400m);
        advance.Months.ShouldHaveSingleItem().Planned.ShouldBe(400m);

        var csv = await (await Client.GetAsync($"/api/company/salaries/export?month={month}&search={person.User.Email}", superAdmin.AccessToken))
            .Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        lines[0].ShouldContain("To pay,Advance,To hand over");
        lines.Single(l => l.Contains(person.User.Email)).ShouldEndWith($",{Cents(row.Total)},400.00,1360.00");
        lines.Single(l => l.Contains("TOTAL")).ShouldEndWith(",400.00,1360.00");
    }

    [Fact]
    public async Task A_closed_month_hands_over_its_pay_less_the_advance_and_the_year_shows_what_it_cost()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var person = await SalariedAsync(superAdmin);

        // 500 given in June and taken in full from June's pay; the API only records advances this month, so it is inserted
        var id = await _steps.ScalarAsync<long>(
            "INSERT INTO salary_advances (user_id, amount, currency, status, created_at, created_by_id, decided_at, decided_by_id, given_on) " +
            "VALUES (@u, 500, 'USD', 'approved', UTC_TIMESTAMP(3), @by, UTC_TIMESTAMP(3), @by, '2026-06-10'); SELECT LAST_INSERT_ID()",
            ("@u", person.User.Id), ("@by", superAdmin.User.Id));
        await ExecuteAsync("INSERT INTO salary_advance_repayments (advance_id, month, amount) VALUES (@id, '2026-06-01', 500)", ("@id", id));

        var june = await ListAsync(superAdmin, person, "2026-06");
        june.InProgress.ShouldBeFalse();
        var row = june.Rows.Items.ShouldHaveSingleItem();
        row.Total.ShouldBe(1760m);
        row.AdvanceRepayment.ShouldBe(500m);
        row.ToHandOver.ShouldBe(1260m);
        var total = june.Totals.ShouldHaveSingleItem();
        (total.Total, total.AdvanceRepayment, total.ToHandOver).ShouldBe((1760m, 500m, 1260m));

        // the months around it hand over everything
        var may = (await ListAsync(superAdmin, person, "2026-05")).Rows.Items.ShouldHaveSingleItem();
        may.AdvanceRepayment.ShouldBeNull();
        may.ToHandOver.ShouldBe(may.Total);

        // the year still shows what each month cost
        var year = await OkAsync<SalaryYearResponse>(Client.GetAsync($"/api/company/salaries/year?year=2026&search={person.User.Email}", superAdmin.AccessToken));
        year.Rows.ShouldHaveSingleItem().Months.Single(m => m.Month == new DateOnly(2026, 6, 1)).Total.ShouldBe(1760m);
    }

    [Fact]
    public async Task Only_a_written_off_advance_is_a_company_expense_on_the_day_it_is_settled()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var nextMonth = new DateOnly(CompanyToday().Year, CompanyToday().Month, 1).AddMonths(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // May 2023: nobody else settles anything then, and nobody was on a salary. Settling is dated today, so it is moved there.
        const string period = "from=2023-05-01&to=2023-05-31";
        Task<CompanyOverviewResponse> OverviewAsync() => OkAsync<CompanyOverviewResponse>(Client.GetAsync($"/api/company/overview?{period}", token));
        async Task<CompanyMonthResponse> MonthAsync()
            => (await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?year=2023", token)))
                .Single(m => m.Month == new DateOnly(2023, 5, 1));
        async Task SettleAsync(AuthResponse person, decimal amount, string kind, string settledOn)
        {
            // planned for next month, so this month takes nothing and all of it is left to settle
            var advance = await RecordAsync(superAdmin, person, amount, nextMonth);
            (await Client.PostJsonAsync($"/api/company/salary-advances/{advance.Id}/settle", new { kind }, token))
                .IsSuccessStatusCode.ShouldBeTrue();
            await ExecuteAsync("UPDATE salary_advances SET settled_on = @on WHERE id = @id", ("@on", settledOn), ("@id", advance.Id));
        }

        var before = await OverviewAsync();
        var monthBefore = await MonthAsync();
        before.AdvanceWriteOffs.ShouldBe(0m);

        // what a person paid back in cash costs nothing
        await SettleAsync(await SalariedAsync(superAdmin), 300m, "cash", "2023-05-12");
        var cash = await OverviewAsync();
        cash.AdvanceWriteOffs.ShouldBe(0m);
        cash.Expenses.ShouldBe(before.Expenses);
        cash.NetProfit.ShouldBe(before.NetProfit);

        // a write-off does
        await SettleAsync(await SalariedAsync(superAdmin), 500m, "written_off", "2023-05-20");
        var after = await OverviewAsync();
        after.AdvanceWriteOffs.ShouldBe(500m);
        after.UnconvertedAdvanceWriteOffs.ShouldBeEmpty();
        after.Expenses.ShouldBe(before.Expenses + 500m);
        after.NetProfit.ShouldBe(before.NetProfit - 500m);
        after.GrossProfit.ShouldBe(before.GrossProfit);
        after.Cost.ShouldBe(before.Cost);
        after.FixedSalaries.ShouldBe(before.FixedSalaries);
        after.PaidLeave.ShouldBe(before.PaidLeave);
        after.OvertimePremium.ShouldBe(before.OvertimePremium);
        after.Expenses.ShouldBe(
            after.Overhead + after.OneOffExpenses + after.FixedSalaries + after.PaidLeave + after.OvertimePremium + after.AdvanceWriteOffs);

        // only on its own day
        (await OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview?from=2023-05-21&to=2023-05-31", token)))
            .AdvanceWriteOffs.ShouldBe(0m);

        // the monthly chart takes it off the net profit, not the profit after overhead
        var month = await MonthAsync();
        month.AdvanceWriteOffs.ShouldBe(500m);
        month.ProfitAfterOverhead.ShouldBe(monthBefore.ProfitAfterOverhead);
        month.NetProfit.ShouldBe(monthBefore.NetProfit - 500m);

        // one in a currency with no exchange rate is listed apart, not counted as 0
        await ExecuteAsync("UPDATE salary_advances SET currency = 'EUR' WHERE settled_on = '2023-05-20'");
        var euros = await OverviewAsync();
        euros.AdvanceWriteOffs.ShouldBe(0m);
        euros.UnconvertedAdvanceWriteOffs.ShouldHaveSingleItem().ShouldBe(new CurrencyTotalResponse("EUR", 500m));
    }
}
