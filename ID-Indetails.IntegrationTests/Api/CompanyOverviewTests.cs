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

        // 1,760 a month is 10 an hour in June (176 paid hours); 6 hours in June (09:00-15:00 in Beirut) cost 60, plus
        // 10 for the paid hour of break.
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1760m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
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
        june.NetProfit.ShouldBe(3930m - june.Overhead - june.AdminSalaries);
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
    public async Task Admins_and_super_admins_salaries_come_off_the_company_s_net_profit()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, partner) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.SuperAdmin, null,
            new { basis = "monthly", amount = 1100m, currency = "USD", effectiveFrom = "2026-01-01" });
        await _steps.SetJoinedOnAsync(partner.User.Id, "2026-01-01");
        // an admin's fixed salary is a company cost too: admins log no hours either
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design,
            new { basis = "monthly", amount = 2000m, currency = "USD", effectiveFrom = "2026-01-01" });
        await _steps.SetJoinedOnAsync(admin.User.Id, "2026-01-01");

        // all of March 2026, then its first 16 of 31 days
        var march = await OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview?from=2026-03-01&to=2026-03-31", superAdmin.AccessToken));
        march.AdminSalaries.ShouldBe(3100m);
        march.UnconvertedAdminSalaries.ShouldBeEmpty();
        march.NetProfit.ShouldBe(march.GrossProfit - march.Overhead - 3100m);
        var half = await OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview?from=2026-03-01&to=2026-03-16", superAdmin.AccessToken));
        half.AdminSalaries.ShouldBe(1600m);

        // the monthly chart takes them off a whole month at a time, after overhead
        var year = await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?year=2026", superAdmin.AccessToken));
        var marchMonth = year.Single(m => m.Month == new DateOnly(2026, 3, 1));
        marchMonth.AdminSalaries.ShouldBe(3100m);
        marchMonth.NetProfit.ShouldBe(marchMonth.ProfitAfterOverhead - 3100m);
        year.Single(m => m.Month == new DateOnly(2026, 1, 1)).AdminSalaries.ShouldBe(3100m);

        // no project carries it: the businesses' costs don't move
        march.Domains.ShouldAllBe(d => d.Cost == 0m);
    }

    [Fact]
    public async Task A_promotion_moves_only_the_days_from_then_on_from_project_labour_to_the_company()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await _steps.SetJoinedOnAsync(designer.User.Id, "2026-01-01");
        // 1,600 a month is 10 an hour in February 2026 (20 working days of 8 paid hours).
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1600m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 10_000m);
        const string entrySql = "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Drawings', @start, @end, UTC_TIMESTAMP(3))";
        // 6 hours on Tuesday 10 February (08:00-14:00 in Beirut) cost 60, plus 10 for the paid hour of break.
        await ExecuteAsync(entrySql, ("@u", designer.User.Id), ("@p", project.Id), ("@start", "2026-02-10 06:00:00"), ("@end", "2026-02-10 12:00:00"));

        async Task<decimal> LabourAsync()
            => (await OkAsync<ProjectResponse>(Client.GetAsync($"/api/projects/{project.Id}", superAdmin.AccessToken))).Money!.Cost.Labour;
        Task<CompanyOverviewResponse> FebruaryAsync()
            => OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview?from=2026-02-01&to=2026-02-28", superAdmin.AccessToken));

        (await LabourAsync()).ShouldBe(70m);
        var before = await FebruaryAsync();

        // Promoted today: February was worked as an employee, so neither the project nor the company's February moves.
        (await Client.PutJsonAsync($"/api/employees/{designer.User.Id}/role", new { role = RoleCodes.Admin }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        (await LabourAsync()).ShouldBe(70m);
        var after = await FebruaryAsync();
        after.AdminSalaries.ShouldBe(before.AdminSalaries);
        after.Domains.Single(d => d.Domain == DomainCodes.Design).Cost.ShouldBe(70m);

        // Promoted on Monday 16 February instead (the change the API recorded, moved back): an hour worked after it costs
        // the project nothing, and its 10 working days from then on are a fixed salary, 800, on the company.
        const string moveSql = "UPDATE activity_events SET occurred_at = @at WHERE kind = @kind AND subject_type = 'user' AND subject_id = @u";
        await ExecuteAsync(moveSql, ("@at", "2026-02-16 08:00:00"), ("@kind", ActivityEventKinds.RoleChanged), ("@u", designer.User.Id));
        await ExecuteAsync(entrySql, ("@u", designer.User.Id), ("@p", project.Id), ("@start", "2026-02-17 06:00:00"), ("@end", "2026-02-17 12:00:00"));
        (await LabourAsync()).ShouldBe(70m);
        (await FebruaryAsync()).AdminSalaries.ShouldBe(before.AdminSalaries + 800m);

        // back to today, so the other tests' months keep only their own admins
        await ExecuteAsync(moveSql, ("@at", DateTime.UtcNow), ("@kind", ActivityEventKinds.RoleChanged), ("@u", designer.User.Id));
    }

    [Fact]
    public async Task Company_overhead_is_charged_to_the_period_by_calendar_days()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        (await Client.PostJsonAsync("/api/company/overhead",
            new { name = "Office rent", groupId = 2, currency = "USD", amount = 3000m }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        var overview = await OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview", superAdmin.AccessToken));

        // This month to date: its elapsed share of 3,000 (at least 1 day's worth) is charged.
        overview.Overhead.ShouldBeGreaterThan(0m);
        overview.Overhead.ShouldBeLessThanOrEqualTo(3000m);
        overview.NetProfit.ShouldBe(overview.GrossProfit - overview.Overhead - overview.AdminSalaries);

        var months = await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?months=1", superAdmin.AccessToken));
        months.ShouldHaveSingleItem().Overhead.ShouldBeGreaterThanOrEqualTo(3000m);
    }
}
