using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.DTO.Response.Payroll;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Dated pay changes: a change dated D replaces every active entry from D on, and pay is per day. June 2026 has 22
/// working days of 8 paid hours: 10 before Monday the 15th and 12 from it. Beirut is UTC+3 in June.
/// </summary>
public class PayChangeTests(ApiFactory api) : IClassFixture<ApiFactory>
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

    private async Task<AuthResponse> JoinedAsync(AuthResponse superAdmin, string domain)
    {
        var (_, _, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, domain);
        await _steps.SetJoinedOnAsync(person.User.Id, "2026-01-01");
        return person;
    }

    private Task<PayResponse> PayAsync(AuthResponse superAdmin, long userId, decimal amount, string from, string currency = "USD")
        => OkAsync<PayResponse>(Client.PostJsonAsync($"/api/employees/{userId}/pay", new { amount, currency, effectiveFrom = from }, superAdmin.AccessToken));

    private Task<SalaryDetailResponse> JuneAsync(AuthResponse superAdmin, long userId)
        => OkAsync<SalaryDetailResponse>(Client.GetAsync($"/api/company/salaries/{userId}?month=2026-06", superAdmin.AccessToken));

    [Fact]
    public async Task A_change_replaces_every_entry_from_its_day_and_June_is_paid_day_by_day()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        // site staff log no hours, so June is the salary alone
        var staff = await JoinedAsync(superAdmin, DomainCodes.Execution);
        await PayAsync(superAdmin, staff.User.Id, 1760m, "2026-01-01");
        await PayAsync(superAdmin, staff.User.Id, 2112m, "2026-06-15");

        // 10 days of 1,760's share (800) and 12 of 2,112's (1,152)
        var june = await JuneAsync(superAdmin, staff.User.Id);
        june.Salary.Total.ShouldBe(1952m);
        june.Salary.PayChanged.ShouldBeTrue();
        june.Parts.Select(p => (p.From, p.To, p.Salary, p.Days, p.Pay)).ShouldBe([
            (new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 12), 1760m, 10, 800m),
            (new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 30), 2112m, 12, 1152m),
        ]);

        // a mistake is fixed by a change on its day: 2,000 from the 15th replaces 2,112
        await PayAsync(superAdmin, staff.User.Id, 2000m, "2026-06-15");
        (await JuneAsync(superAdmin, staff.User.Id)).Salary.Total.ShouldBe(1890.91m);

        // an earlier change replaces everything from its day on, and leaves what came before
        await PayAsync(superAdmin, staff.User.Id, 1800m, "2026-03-01");
        var corrected = await JuneAsync(superAdmin, staff.User.Id);
        corrected.Salary.Total.ShouldBe(1800m);
        corrected.Parts.ShouldHaveSingleItem().Days.ShouldBe(22);
        corrected.Salary.PayChanged.ShouldBeFalse();

        var history = await OkAsync<List<PayResponse>>(Client.GetAsync($"/api/employees/{staff.User.Id}/pay", superAdmin.AccessToken));
        history.Select(p => (p.EffectiveFrom, p.Amount, Replaced: p.SupersededAt is not null)).ShouldBe([
            (new DateOnly(2026, 6, 15), 2000m, true),
            (new DateOnly(2026, 6, 15), 2112m, true),
            (new DateOnly(2026, 3, 1), 1800m, false),
            (new DateOnly(2026, 1, 1), 1760m, false),
        ]);
        history.Where(p => p.SupersededAt is not null).ShouldAllBe(p => p.SupersededById == superAdmin.User.Id);
        (await OkAsync<EmployeeResponse>(Client.GetAsync($"/api/employees/{staff.User.Id}", superAdmin.AccessToken)))
            .CurrentPay!.Amount.ShouldBe(1800m);
    }

    [Fact]
    public async Task A_change_of_currency_starts_on_the_first()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var staff = await JoinedAsync(superAdmin, DomainCodes.Execution);
        await PayAsync(superAdmin, staff.User.Id, 1760m, "2026-01-01");

        await (await Client.PostJsonAsync($"/api/employees/{staff.User.Id}/pay",
                new { amount = 157_520_000m, currency = "LBP", effectiveFrom = "2026-06-15" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.PayCurrencyChangeNotFirst);
        (await PayAsync(superAdmin, staff.User.Id, 157_520_000m, "2026-06-01", "LBP")).Currency.ShouldBe("LBP");

        var june = await JuneAsync(superAdmin, staff.User.Id);
        june.Salary.Currency.ShouldBe("LBP");
        june.Salary.Total.ShouldBe(157_520_000m);
    }

    [Fact]
    public async Task The_preview_shows_each_month_and_project_before_and_after_and_keeps_nothing()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var designer = await JoinedAsync(superAdmin, DomainCodes.Design);
        await PayAsync(superAdmin, designer.User.Id, 1760m, "2026-01-01");
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 10_000m);
        // Monday 22 June, 9 to 5: 7h within hours, 1h making up the short month, and the paid hour of break
        await ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Drawings', '2026-06-22 06:00:00', '2026-06-22 14:00:00', UTC_TIMESTAMP(3))",
            ("@u", designer.User.Id), ("@p", project.Id));

        var url = $"/api/employees/{designer.User.Id}/pay/preview";
        var preview = await OkAsync<PayPreviewResponse>(Client.PostJsonAsync(url,
            new { amount = 2112m, currency = "USD", effectiveFrom = "2026-06-15" }, superAdmin.AccessToken));

        preview.EffectiveFrom.ShouldBe(new DateOnly(2026, 6, 15));
        preview.Replaces.ShouldBeEmpty();
        var june = preview.Months[0];
        june.Month.ShouldBe(new DateOnly(2026, 6, 1));
        june.CurrencyBefore.ShouldBe("USD");
        june.CurrencyAfter.ShouldBe("USD");
        june.SalaryAfter!.Value.ShouldBeGreaterThan(june.SalaryBefore!.Value);
        // 9 paid hours on the 22nd: at 10 an hour before, 12 after
        var moved = preview.Projects.ShouldHaveSingleItem();
        moved.ProjectId.ShouldBe(project.Id);
        moved.LabourBefore.ShouldBe(90m);
        moved.LabourAfter.ShouldBe(108m);
        moved.ProfitBefore.ShouldBe(9910m);
        moved.ProfitAfter.ShouldBe(9892m);
        moved.ReachesZero.ShouldBeFalse();

        // a change dated before the salary would replace it
        (await OkAsync<PayPreviewResponse>(Client.PostJsonAsync(url, new { amount = 1800m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken)))
            .Replaces.ShouldHaveSingleItem().Amount.ShouldBe(1760m);

        // nothing was kept
        (await OkAsync<List<PayResponse>>(Client.GetAsync($"/api/employees/{designer.User.Id}/pay", superAdmin.AccessToken)))
            .ShouldHaveSingleItem().Amount.ShouldBe(1760m);
        (await JuneAsync(superAdmin, designer.User.Id)).Salary.Total.ShouldBe(june.SalaryBefore!.Value);
    }

    [Fact]
    public async Task The_preview_shows_project_money_only_to_those_who_see_it()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var designer = await JoinedAsync(superAdmin, DomainCodes.Design);
        await PayAsync(superAdmin, designer.User.Id, 1760m, "2026-01-01");
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 10_000m);
        await ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Drawings', '2026-06-22 06:00:00', '2026-06-22 14:00:00', UTC_TIMESTAMP(3))",
            ("@u", designer.User.Id), ("@p", project.Id));
        var (_, _, admin) = await _steps.MoneyAdminAsync(superAdmin, DomainCodes.Design);
        (await Client.PutJsonAsync($"/api/users/{admin.User.Id}/permissions",
                new { changes = new[] { new { code = PermissionCodes.FinancialView, action = "revoke", reason = "Pay only" } } }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var preview = await OkAsync<PayPreviewResponse>(Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay/preview",
            new { amount = 2112m, currency = "USD", effectiveFrom = "2026-06-15" }, admin.AccessToken));
        preview.Projects.ShouldBeEmpty();
        preview.Months.ShouldNotBeEmpty();

        await (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay/preview",
                new { amount = 2112m, currency = "USD" }, designer.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
    }
}
