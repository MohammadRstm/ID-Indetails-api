using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Payroll;
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
        june.NetProfit.ShouldBe(3930m - june.Overhead - june.FixedSalaries - june.PaidLeave - june.OvertimePremium);
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

    /// <summary>
    /// Money in both domains: half an hour from <paramref name="workUtc"/> (09:00 in Beirut on a weekday) by someone paid
    /// 10.01 an hour, which with the paid hour of break is 15.015 of labour, a fraction of a cent; a Design expense and
    /// payment; a site expense and payment.
    /// </summary>
    private async Task MoneyInBothDomainsAsync(
        AuthResponse superAdmin, DateTime workUtc, string spentOn, string receivedOn, decimal designReceived, decimal siteReceived)
    {
        var token = superAdmin.AccessToken;
        var projects = new ProjectTestSteps(_steps);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "hourly", amount = 10.01m, currency = "USD", effectiveFrom = "2025-01-01" }, token))
            .IsSuccessStatusCode.ShouldBeTrue();

        var project = await projects.CreateProjectAsync(token, DomainCodes.Design, revenue: 10_000m);
        await ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Drawings', @start, @end, UTC_TIMESTAMP(3))",
            ("@u", designer.User.Id), ("@p", project.Id), ("@start", workUtc), ("@end", workUtc.AddMinutes(30)));
        await OkAsync<ExpenseResponse>(Client.PostJsonAsync($"/api/projects/{project.Id}/expenses",
            new { label = "Print run", amount = 40m, currency = "USD", spentOn }, token));
        await OkAsync<object>(Client.PostJsonAsync($"/api/projects/{project.Id}/payments",
            new { amount = designReceived, currency = "USD", receivedOn }, token));

        var site = await projects.CreateProjectAsync(token, DomainCodes.Execution);
        await OkAsync<object>(Client.PostJsonAsync($"/api/projects/{site.Id}/site-expenses",
            new { items = new[] { new { name = "Cement", amount = 250m, spentOn } } }, token));
        await OkAsync<object>(Client.PostJsonAsync($"/api/projects/{site.Id}/payments",
            new { amount = siteReceived, currency = "USD", receivedOn }, token));
    }

    [Fact]
    public async Task Each_domain_s_money_is_what_its_Financials_give_for_the_same_period()
    {
        // August and September 2025, when no other test here has project money
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var token = superAdmin.AccessToken;
        var projects = new ProjectTestSteps(_steps);
        await MoneyInBothDomainsAsync(superAdmin, new DateTime(2025, 8, 12, 6, 0, 0), "2025-08-14", "2025-09-03", 500m, 600m);

        // and a Design invoice of 3,000, 1,000 of it paid, so something is outstanding
        var project = await projects.CreateProjectAsync(token, DomainCodes.Design, revenue: 10_000m);
        await projects.SetUpBillingAsync(token, DomainCodes.Design);
        var draft = await OkAsync<InvoiceResponse>(Client.PostJsonAsync($"/api/projects/{project.Id}/invoices",
            new { lines = new[] { new { description = "Stage 1", amount = 3000m } } }, token));
        (await Client.PostJsonAsync($"/api/invoices/{draft.Id}/issue", new { issuedOn = "2025-08-18" }, token))
            .IsSuccessStatusCode.ShouldBeTrue();
        await OkAsync<object>(Client.PostJsonAsync($"/api/projects/{project.Id}/payments",
            new { amount = 1000m, currency = "USD", receivedOn = "2025-08-25", invoiceId = draft.Id }, token));

        const string period = "from=2025-08-01&to=2025-09-30";
        var overview = await OkAsync<CompanyOverviewResponse>(Client.GetAsync($"/api/company/overview?{period}", token));
        var profits = 0m;
        foreach (var domain in DomainCodes.Businesses)
        {
            var summary = await OkAsync<FinanceSummaryResponse>(Client.GetAsync($"/api/finance/{domain}/summary?{period}", token));
            summary.Revenue.ShouldBeGreaterThan(0m);
            var figures = overview.Domains.Single(d => d.Domain == domain);
            figures.Revenue.ShouldBe(summary.Revenue);
            figures.Cost.ShouldBe(summary.Cost.Total);
            figures.Profit.ShouldBe(summary.Profit);
            figures.Margin.ShouldBe(summary.Margin);
            figures.Outstanding.ShouldBe(summary.Billing.Outstanding);
            profits += summary.Profit;
        }

        overview.GrossProfit.ShouldBe(profits);
    }

    [Fact]
    public async Task A_month_s_profit_before_overhead_is_the_overview_s_gross_profit_for_that_month()
    {
        // October 2025, when no other test here has project money. The month adds up each domain's period profit, as the
        // overview does, so the two agree to the cent.
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await MoneyInBothDomainsAsync(superAdmin, new DateTime(2025, 10, 14, 6, 0, 0), "2025-10-15", "2025-10-20", 2500m, 700m);

        var october = await OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview?from=2025-10-01&to=2025-10-31", superAdmin.AccessToken));
        var year = await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?year=2025", superAdmin.AccessToken));
        var month = year.Single(m => m.Month == new DateOnly(2025, 10, 1));

        october.Revenue.ShouldBeGreaterThan(0m);
        month.Revenue.ShouldBe(october.Revenue);
        month.ProfitBeforeOverhead.ShouldBe(october.GrossProfit);
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
        march.FixedSalaries.ShouldBe(3100m);
        march.UnconvertedFixedSalaries.ShouldBeEmpty();
        march.NetProfit.ShouldBe(march.GrossProfit - march.Overhead - 3100m);
        // the phone app still reads them under their old name
        march.AdminSalaries.ShouldBe(march.FixedSalaries);
        march.UnconvertedAdminSalaries.ShouldBeEmpty();
        var half = await OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview?from=2026-03-01&to=2026-03-16", superAdmin.AccessToken));
        half.FixedSalaries.ShouldBe(1600m);

        // the monthly chart takes them off a whole month at a time, after overhead
        var year = await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?year=2026", superAdmin.AccessToken));
        var marchMonth = year.Single(m => m.Month == new DateOnly(2026, 3, 1));
        marchMonth.FixedSalaries.ShouldBe(3100m);
        marchMonth.NetProfit.ShouldBe(marchMonth.ProfitAfterOverhead - 3100m);
        year.Single(m => m.Month == new DateOnly(2026, 1, 1)).FixedSalaries.ShouldBe(3100m);

        // no project carries it: the businesses' costs don't move
        march.Domains.ShouldAllBe(d => d.Cost == 0m);
    }

    [Fact]
    public async Task A_promotion_makes_its_month_a_fixed_salary_and_leaves_the_months_before_alone()
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
        async Task<SalaryRowResponse> FebruarySalaryAsync()
            => (await OkAsync<SalaryDetailResponse>(Client.GetAsync($"/api/company/salaries/{designer.User.Id}?month=2026-02", superAdmin.AccessToken))).Salary;

        (await LabourAsync()).ShouldBe(70m);
        var before = await FebruaryAsync();
        var salaryBefore = await FebruarySalaryAsync();
        salaryBefore.TracksHours.ShouldBeTrue();

        // Promoted today: the caller is told their pay is a fixed salary from the first of this month, starting from the
        // pay in force. February was worked as an employee, so the project, the company and Salaries keep it as it was.
        var promoted = await OkAsync<EmployeeResponse>(Client.PutJsonAsync($"/api/employees/{designer.User.Id}/role",
            new { role = RoleCodes.Admin }, superAdmin.AccessToken));
        promoted.FixedPayFrom.ShouldNotBeNull().Day.ShouldBe(1);
        promoted.CurrentPay.ShouldNotBeNull().Amount.ShouldBe(1600m);
        (await LabourAsync()).ShouldBe(70m);
        var after = await FebruaryAsync();
        after.AdminSalaries.ShouldBe(before.AdminSalaries);
        after.Domains.Single(d => d.Domain == DomainCodes.Design).Cost.ShouldBe(70m);
        var salaryAfter = await FebruarySalaryAsync();
        salaryAfter.TracksHours.ShouldBeTrue();
        salaryAfter.Total.ShouldBe(salaryBefore.Total);

        // Promoted on Monday 16 February instead (the change the API recorded, moved back): all of February is a fixed
        // salary, so its hours cost the project nothing, the whole 1,600 is a company cost, and Salaries pays it without
        // counting hours.
        const string moveSql = "UPDATE activity_events SET occurred_at = @at WHERE kind = @kind AND subject_type = 'user' AND subject_id = @u";
        await ExecuteAsync(moveSql, ("@at", "2026-02-16 08:00:00"), ("@kind", ActivityEventKinds.RoleChanged), ("@u", designer.User.Id));
        (await LabourAsync()).ShouldBe(0m);
        (await FebruaryAsync()).AdminSalaries.ShouldBe(before.AdminSalaries + 1600m);
        var fixedFebruary = await FebruarySalaryAsync();
        fixedFebruary.TracksHours.ShouldBeFalse();
        fixedFebruary.Total.ShouldBe(1600m);

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
        overview.NetProfit.ShouldBe(overview.GrossProfit - overview.Expenses);
        overview.Expenses.ShouldBe(overview.Overhead + overview.OneOffExpenses + overview.FixedSalaries + overview.PaidLeave + overview.OvertimePremium);

        var months = await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?months=1", superAdmin.AccessToken));
        months.ShouldHaveSingleItem().Overhead.ShouldBeGreaterThanOrEqualTo(3000m);
    }

    [Fact]
    public async Task Site_staff_salaries_are_fixed_salaries_and_paid_leave_comes_off_the_company_too()
    {
        // April 2025 has 22 working days of 8 paid hours, so 1,760 a month is 10 an hour. Both people left at the end of
        // 2025, so the other tests' 2026 months keep only their own people.
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, siteStaff) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        foreach (var (person, amount) in new[] { (siteStaff, 1500m), (designer, 1760m) })
        {
            await _steps.SetJoinedOnAsync(person.User.Id, "2025-01-01");
            (await Client.PostJsonAsync($"/api/employees/{person.User.Id}/pay",
                new { basis = "monthly", amount, currency = "USD", effectiveFrom = "2025-01-01" }, superAdmin.AccessToken))
                .IsSuccessStatusCode.ShouldBeTrue();
        }

        // two paid hours and a paid day off (Tuesday 15 April, 8 paid hours) are paid leave: 100; the unpaid hour is
        // deducted from the designer's pay instead
        object[] excuses =
        [
            new { month = "2025-04", kind = "minutes", minutes = 120, isPaid = true },
            new { month = "2025-04", kind = "days", days = new[] { "2025-04-15" }, isPaid = true },
            new { month = "2025-04", kind = "minutes", minutes = 60, isPaid = false },
        ];
        foreach (var excuse in excuses)
        {
            var added = await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/hour-excuses", excuse, superAdmin.AccessToken);
            added.IsSuccessStatusCode.ShouldBeTrue(await added.Content.ReadAsStringAsync());
        }

        await _steps.SetLeftAtAsync(siteStaff.User.Id, "2025-12-31 15:00:00");
        await _steps.SetLeftAtAsync(designer.User.Id, "2025-12-31 15:00:00");

        // the site staff log no hours, so their whole salary is a fixed salary; the designer's hours are project cost,
        // and only their paid leave comes off the company
        var april = await OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview?from=2025-04-01&to=2025-04-30", superAdmin.AccessToken));
        april.FixedSalaries.ShouldBe(1500m);
        april.PaidLeave.ShouldBe(100m);
        april.UnconvertedPaidLeave.ShouldBeEmpty();
        april.AdminSalaries.ShouldBe(1500m);
        april.NetProfit.ShouldBe(april.GrossProfit - april.Overhead - 1500m - 100m);
        april.Domains.ShouldAllBe(d => d.Cost == 0m);

        var year = await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?year=2025", superAdmin.AccessToken));
        var aprilMonth = year.Single(m => m.Month == new DateOnly(2025, 4, 1));
        (aprilMonth.FixedSalaries, aprilMonth.PaidLeave).ShouldBe((1500m, 100m));
        aprilMonth.NetProfit.ShouldBe(aprilMonth.ProfitAfterOverhead - 1600m);
        year.Single(m => m.Month == new DateOnly(2025, 3, 1)).PaidLeave.ShouldBe(0m);
    }

    [Fact]
    public async Task The_overtime_premium_past_a_salary_s_threshold_comes_off_the_company_too()
    {
        // From November 2026 a salary's overtime is paid only past 184 counted hours. November 2026 has 21 working days
        // of 8 paid hours (7 logged, with the paid break): 1,680 a month is 10 an hour. Overtime is set at 15 an hour (the
        // seeded working time's multiplier is 1).
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await _steps.SetJoinedOnAsync(designer.User.Id, "2026-01-01");
        (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/pay",
            new { basis = "monthly", amount = 1680m, currency = "USD", overtimeRate = 15m, effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design, revenue: 10_000m);

        // 7 hours on every working day (147, no overtime) and 5 on eight weekend days (40, all overtime): 187 counted, 3
        // past the threshold. Salaries pays them at 15 an hour (45) and the project carries them at 10 (30): the other 15
        // is the premium.
        const string entrySql = "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Drawings', @start, @end, UTC_TIMESTAMP(3))";
        var weekendDays = 0;
        for (var day = new DateOnly(2026, 11, 1); day.Month == 11; day = day.AddDays(1))
        {
            var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (weekend && ++weekendDays > 8)
            {
                continue;
            }

            var start = day.ToDateTime(new TimeOnly(7, 0)); // 09:00 in Beirut
            await ExecuteAsync(entrySql, ("@u", designer.User.Id), ("@p", project.Id), ("@start", start), ("@end", start.AddHours(weekend ? 5 : 7)));
        }

        var year = await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?year=2026", superAdmin.AccessToken));
        var november = year.Single(m => m.Month == new DateOnly(2026, 11, 1));
        november.OvertimePremium.ShouldBe(15m);
        november.NetProfit.ShouldBe(november.ProfitAfterOverhead - november.FixedSalaries - november.PaidLeave - 15m);
        year.Single(m => m.Month == new DateOnly(2026, 10, 1)).OvertimePremium.ShouldBe(0m);
    }

    [Fact]
    public async Task One_off_expenses_count_in_full_on_their_day_at_that_day_s_rate_and_come_off_the_net_profit()
    {
        // March 2024, when no other test here has money. Rows are inserted: the API only dates one-offs this month.
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        await ExecuteAsync("INSERT INTO exchange_rates (currency, rate_per_usd, effective_from, created_at) VALUES ('LBP', 90000, '2024-03-01', UTC_TIMESTAMP())");
        await ExecuteAsync("INSERT INTO exchange_rates (currency, rate_per_usd, effective_from, created_at) VALUES ('LBP', 100000, '2024-03-20', UTC_TIMESTAMP())");
        foreach (var (name, currency, amount, day) in new[]
        {
            ("Refrigerator", "USD", 200m, "2024-03-05"),
            ("Generator part", "LBP", 900_000m, "2024-03-10"), // 10 at 90,000
            ("Kettle", "LBP", 1_000_000m, "2024-03-25"), // 10 at 100,000
            ("Euro invoice", "EUR", 70m, "2024-03-12"), // no rate
        })
        {
            await ExecuteAsync(
                "INSERT INTO one_off_expenses (name, group_id, currency, amount, spent_on, created_at) VALUES (@n, 1, @c, @a, @d, UTC_TIMESTAMP(3))",
                ("@n", name), ("@c", currency), ("@a", amount), ("@d", DateTime.Parse(day)));
        }

        var march = await OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview?from=2024-03-01&to=2024-03-31", superAdmin.AccessToken));
        march.OneOffExpenses.ShouldBe(220m);
        march.UnconvertedOneOffExpenses.ShouldHaveSingleItem().ShouldBe(new CurrencyTotalResponse("EUR", 70m));
        march.Expenses.ShouldBe(march.Overhead + march.OneOffExpenses + march.FixedSalaries + march.PaidLeave + march.OvertimePremium);
        march.NetProfit.ShouldBe(march.GrossProfit - march.Expenses);

        // a period counts only the one-offs spent inside it
        var later = await OkAsync<CompanyOverviewResponse>(Client.GetAsync("/api/company/overview?from=2024-03-06&to=2024-03-31", superAdmin.AccessToken));
        later.OneOffExpenses.ShouldBe(20m);

        var year = await OkAsync<List<CompanyMonthResponse>>(Client.GetAsync("/api/company/overview/monthly?year=2024", superAdmin.AccessToken));
        var month = year.Single(m => m.Month == new DateOnly(2024, 3, 1));
        month.OneOffExpenses.ShouldBe(220m);
        month.ProfitAfterOverhead.ShouldBe(month.ProfitBeforeOverhead - month.Overhead - 220m);
        month.NetProfit.ShouldBe(month.ProfitAfterOverhead - month.FixedSalaries - month.PaidLeave - month.OvertimePremium);
        year.Single(m => m.Month == new DateOnly(2024, 2, 1)).OneOffExpenses.ShouldBe(0m);
    }
}
