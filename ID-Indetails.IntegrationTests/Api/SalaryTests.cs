using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Enums;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// June 2026: 22 Monday–Friday working days under the seeded 9–5 week (7h required, a paid 1h break).
/// Beirut is UTC+3 in June, so 06:00 UTC is 09:00 there.
/// </summary>
public class SalaryTests(ApiFactory api) : IClassFixture<ApiFactory>
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

    /// <summary>Someone who joined on 1 January 2026 with 1,733.30 a month (10 an hour).</summary>
    private async Task<AuthResponse> SalariedAsync(AuthResponse superAdmin, string domain)
    {
        var (_, _, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, domain);
        await _steps.SetJoinedOnAsync(person.User.Id, "2026-01-01");
        (await Client.PostJsonAsync($"/api/employees/{person.User.Id}/pay",
            new { basis = "monthly", amount = 1733.30m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        return person;
    }

    [Fact]
    public async Task A_closed_month_deducts_the_shortfall_and_excused_days_restore_the_full_salary()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var designer = await SalariedAsync(superAdmin, DomainCodes.Design);
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        // One 8-hour day on Monday 1 June: 7h within hours and 1h beyond the day's requirement.
        await ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Drawings', '2026-06-01 06:00:00', '2026-06-01 14:00:00', UTC_TIMESTAMP(3))",
            ("@u", designer.User.Id), ("@p", project.Id));

        var url = $"/api/company/salaries/{designer.User.Id}?month=2026-06";
        var short_ = await OkAsync<SalaryDetailResponse>(Client.GetAsync(url, superAdmin.AccessToken));

        // 21 missed days, 8,760 minutes short after the made-up hour: 20 whole days still owed (each costs the
        // full paid day, 8h) and 6h of the last one.
        short_.Salary.ExpectedMinutes.ShouldBe(22 * 420);
        short_.Salary.MadeUpMinutes.ShouldBe(60);
        short_.Salary.ShortMinutes.ShouldBe(8760);
        short_.Salary.Flagged.ShouldBeTrue();
        short_.MissedDays.Count.ShouldBe(21);
        short_.DeductionMinutes.ShouldBe(8760 + 20 * 60);
        short_.Salary.BasePay.ShouldBe(73.30m);
        short_.OvertimeWork.ShouldHaveSingleItem().MadeUpMinutes.ShouldBe(60);
        short_.WorkingDays.Count.ShouldBe(22);
        short_.WorkingDays.ShouldAllBe(d => d.RequiredMinutes == 420 && d.Date.DayOfWeek != DayOfWeek.Saturday && d.Date.DayOfWeek != DayOfWeek.Sunday);

        // where the hours went: the day's 7h in schedule and the made-up hour, both at 10 an hour
        var onProject = short_.Projects.ShouldHaveSingleItem();
        onProject.ProjectId.ShouldBe(project.Id);
        onProject.Title.ShouldBe(project.Title);
        onProject.Entries.ShouldBe(1);
        onProject.NormalMinutes.ShouldBe(420);
        onProject.MadeUpMinutes.ShouldBe(60);
        onProject.OvertimeMinutes.ShouldBe(0);
        onProject.Pay.ShouldBe(80m);
        onProject.Currency.ShouldBe("USD");

        var list = await OkAsync<SalariesResponse>(Client.GetAsync($"/api/company/salaries?month=2026-06&search={designer.User.Email}", superAdmin.AccessToken));
        list.Summary.ExpectedMinutes.ShouldBe(22 * 420);

        // Excusing the other days clears the shortfall; the extra hour is then paid as overtime at 1x (the seeded multiplier).
        var otherDays = Enumerable.Range(2, 29).Select(d => $"2026-06-{d:00}").ToArray();
        var excuse = await OkAsync<HourExcuseResponse>(Client.PostJsonAsync($"/api/employees/{designer.User.Id}/hour-excuses",
            new { month = "2026-06", kind = "days", days = otherDays }, superAdmin.AccessToken));
        excuse.Note.ShouldBe("Day off");
        // the feed says how much was excused
        (await _steps.ScalarAsync<int>(
            "SELECT JSON_EXTRACT(payload, '$.days') FROM activity_events WHERE kind = 'hours_excused' AND subject_id = @u", ("@u", designer.User.Id)))
            .ShouldBe(29);

        var excused = await OkAsync<SalaryDetailResponse>(Client.GetAsync(url, superAdmin.AccessToken));
        excused.Salary.ShortMinutes.ShouldBe(0);
        excused.Salary.BasePay.ShouldBe(1733.30m);
        excused.Salary.OvertimePay.ShouldBe(10m);
        excused.Excuses.ShouldHaveSingleItem();
        excused.Projects.ShouldHaveSingleItem().OvertimeMinutes.ShouldBe(60);
        excused.Projects[0].MadeUpMinutes.ShouldBe(0);

        (await Client.DeleteAsync($"/api/hour-excuses/{excuse.Id}", superAdmin.AccessToken)).StatusCode
            .ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await OkAsync<SalaryDetailResponse>(Client.GetAsync(url, superAdmin.AccessToken))).Salary.Flagged.ShouldBeTrue();
    }

    [Fact]
    public async Task Execution_staff_are_paid_their_salary_without_an_hours_ledger()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var siteWorker = await SalariedAsync(superAdmin, DomainCodes.Execution);

        var list = await OkAsync<SalariesResponse>(Client.GetAsync($"/api/company/salaries?month=2026-06&search={siteWorker.User.Email}", superAdmin.AccessToken));

        var row = list.Rows.Items.ShouldHaveSingleItem();
        row.TracksHours.ShouldBeFalse();
        row.ExpectedMinutes.ShouldBeNull();
        list.Summary.ExpectedMinutes.ShouldBeNull();
        row.Total.ShouldBe(1733.30m);
        list.InProgress.ShouldBeFalse();
        (await OkAsync<SalaryDetailResponse>(Client.GetAsync($"/api/company/salaries/{siteWorker.User.Id}?month=2026-06", superAdmin.AccessToken)))
            .Projects.ShouldBeEmpty();
        list.Totals.ShouldHaveSingleItem().Currency.ShouldBe("USD");
    }

    [Fact]
    public async Task A_super_admin_is_paid_their_salary_whatever_hours_they_log()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, partner) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.SuperAdmin, null);
        await _steps.SetJoinedOnAsync(partner.User.Id, "2026-01-01");

        // a super admin's pay can't depend on hours, at a pay change or an invitation
        await (await Client.PostJsonAsync($"/api/employees/{partner.User.Id}/pay",
                new { basis = "hourly", amount = 20m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.SuperAdminPaidMonthly);
        await (await Client.PostJsonAsync("/api/invitations", new
            {
                email = $"super-{Guid.NewGuid():N}@example.test",
                role = RoleCodes.SuperAdmin,
                pay = new { basis = "hourly", amount = 20m, currency = "USD" },
            }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.SuperAdminPaidMonthly);
        (await Client.PostJsonAsync($"/api/employees/{partner.User.Id}/pay",
                new { basis = "monthly", amount = 1733.30m, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();

        // Saturday 6 June, a day off: overtime for anyone else
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        await ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, 'Review', '2026-06-06 06:00:00', '2026-06-06 10:00:00', UTC_TIMESTAMP(3))",
            ("@u", partner.User.Id), ("@p", project.Id));

        var list = await OkAsync<SalariesResponse>(Client.GetAsync($"/api/company/salaries?month=2026-06&search={partner.User.Email}", superAdmin.AccessToken));
        var row = list.Rows.Items.ShouldHaveSingleItem();
        row.Domain.ShouldBeNull();
        row.TracksHours.ShouldBeFalse();
        row.ExpectedMinutes.ShouldBeNull();
        row.OvertimeMinutes.ShouldBe(0);
        row.OvertimePay.ShouldBe(0m);
        row.Total.ShouldBe(1733.30m);
        var detail = await OkAsync<SalaryDetailResponse>(Client.GetAsync($"/api/company/salaries/{partner.User.Id}?month=2026-06", superAdmin.AccessToken));
        detail.PaidBreakPay.ShouldBe(0m);
        detail.Projects.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unpaid_day_off_is_deducted_as_a_full_paid_day()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var siteWorker = await SalariedAsync(superAdmin, DomainCodes.Execution);

        await OkAsync<HourExcuseResponse>(Client.PostJsonAsync($"/api/employees/{siteWorker.User.Id}/hour-excuses",
            new { month = "2026-06", kind = "days", days = new[] { "2026-06-03" }, isPaid = false }, superAdmin.AccessToken));

        var detail = await OkAsync<SalaryDetailResponse>(Client.GetAsync($"/api/company/salaries/{siteWorker.User.Id}?month=2026-06", superAdmin.AccessToken));
        detail.DeductionMinutes.ShouldBe(480);
        detail.Salary.Deduction.ShouldBe(80m);
        detail.Salary.Total.ShouldBe(1653.30m);
    }

    [Fact]
    public async Task The_export_is_a_csv_of_the_filtered_list_with_totals()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var siteWorker = await SalariedAsync(superAdmin, DomainCodes.Execution);

        var response = await Client.GetAsync($"/api/company/salaries/export?month=2026-06&search={siteWorker.User.Email}", superAdmin.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        csv.ShouldContain("Month,Employee,Email");
        csv.ShouldContain(siteWorker.User.Email);
        csv.ShouldContain("TOTAL");
        csv.ShouldContain("1733.30");
    }

    [Fact]
    public async Task Salaries_need_salary_view_and_never_show_a_future_month()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        await (await Client.GetAsync("/api/company/salaries", admin.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        await (await Client.GetAsync("/api/company/salaries?month=2099-01", superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.SalaryMonthInFuture);
        await (await Client.GetAsync("/api/company/salaries?month=June", superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync($"/api/employees/{admin.User.Id}/hour-excuses",
                new { month = "2099-01", kind = "whole_month" }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ExcuseMonthInFuture);
        await (await Client.PostJsonAsync($"/api/employees/{admin.User.Id}/hour-excuses",
                new { month = "2026-06", kind = "days", days = new[] { "2026-07-01" } }, superAdmin.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.PostJsonAsync($"/api/employees/{superAdmin.User.Id}/hour-excuses",
                new { month = "2026-06", kind = "whole_month" }, admin.AccessToken))
            .ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        PayBasis.Monthly.ShouldBe(PayBasis.Monthly);
    }
}
