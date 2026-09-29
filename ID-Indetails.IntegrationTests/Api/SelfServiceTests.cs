using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Payroll;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// The caller's own schedule, hours, overtime and pay under <c>/api/me</c>. June 2026 has 22 Monday–Friday
/// working days under the seeded 9–5 week (7h required, a paid 1h break); Beirut is UTC+3 then, so 06:00 UTC is 09:00.
/// </summary>
public class SelfServiceTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    [Fact]
    public async Task The_schedule_is_the_caller_s_own_week_with_today_s_hours()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, me) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var (_, _, colleague) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);

        // Every day of the week, 8 to 4 (7h required after the paid break), so today is always a working day.
        (await Client.PutJsonAsync($"/api/employees/{me.User.Id}/schedule",
                new { days = Enumerable.Range(0, 7).Select(d => new { weekday = d, startMinute = 480, endMinute = 960 }) }, superAdmin.AccessToken))
            .IsSuccessStatusCode.ShouldBeTrue();
        var nineLocal = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(TodayInBeirut().ToDateTime(new TimeOnly(9, 0)), DateTimeKind.Unspecified), Beirut);
        await AddWorkAsync(me, project.Id, nineLocal, minutes: 60);

        var mine = await OkAsync<MyScheduleResponse>(Client.GetAsync("/api/me/schedule", me.AccessToken));

        mine.Schedule.ShouldSatisfyAllConditions(
            s => s.UserId.ShouldBe(me.User.Id),
            s => s.IsOwn.ShouldBeTrue(),
            s => s.Days.Count.ShouldBe(7),
            s => s.RequiredMinutesPerWeek.ShouldBe(7 * 420));
        mine.Today.ShouldSatisfyAllConditions(
            t => t.IsWorkingDay.ShouldBeTrue(),
            t => t.StartMinute.ShouldBe(480),
            t => t.RequiredMinutes.ShouldBe(420),
            t => t.PaidBreakMinutes.ShouldBe(60),
            t => t.LoggedMinutes.ShouldBe(60),
            t => t.InsideMinutes.ShouldBe(60),
            t => t.LeftMinutes.ShouldBe(360));
        mine.CompanyWeek.BreakMinutes.ShouldBe(60);

        var theirs = await OkAsync<MyScheduleResponse>(Client.GetAsync("/api/me/schedule", colleague.AccessToken));
        theirs.Schedule.ShouldSatisfyAllConditions(s => s.UserId.ShouldBe(colleague.User.Id), s => s.IsOwn.ShouldBeFalse());
        theirs.Today.LoggedMinutes.ShouldBe(0);
    }

    [Fact]
    public async Task The_hours_ledger_runs_from_the_joining_month_with_a_missed_day_and_an_unpaid_day_off()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var me = await JuneMonthAsync(superAdmin, project.Id, "monthly", 1733.30m);

        var hours = await OkAsync<MyHoursResponse>(Client.GetAsync("/api/me/hours", me.AccessToken));

        hours.Applies.ShouldBeTrue();
        hours.Months.Count.ShouldBe(hours.TotalMonths);
        hours.Months[^1].Month.ShouldBe(new DateOnly(2026, 1, 1));
        hours.Months.ShouldBeInOrder(SortDirection.Descending, Comparer<MyLedgerMonthResponse>.Create((a, b) => a.Month.CompareTo(b.Month)));

        // 22 working days: one worked (7h inside plus 1h beyond, which makes up an hour), one unpaid day off, 20 missed.
        var june = hours.Months.Single(m => m.Month == new DateOnly(2026, 6, 1));
        june.ShouldSatisfyAllConditions(
            m => m.Status.ShouldBe(ID_Indetails.api.Business.Payroll.MyTime.LedgerStatus.Short),
            m => m.ExpectedMinutes.ShouldBe(22 * 420),
            m => m.ExpectedFullMonthMinutes.ShouldBe(22 * 420),
            m => m.ExcusedMinutes.ShouldBe(420),
            m => m.InsideMinutes.ShouldBe(420),
            m => m.MadeUpMinutes.ShouldBe(60),
            m => m.ShortMinutes.ShouldBe(22 * 420 - 420 - 420 - 60),
            m => m.MissedDays.Count.ShouldBe(20));

        (await OkAsync<MyHoursResponse>(Client.GetAsync("/api/me/hours?page=2", me.AccessToken))).Months.ShouldBeEmpty();
    }

    [Fact]
    public async Task Overtime_lists_only_the_caller_s_own_entries_day_by_day()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        var me = await JuneMonthAsync(superAdmin, project.Id, "monthly", 1733.30m);
        var colleague = await JuneMonthAsync(superAdmin, project.Id, "hourly", 12m);
        await AddWorkAsync(colleague, project.Id, new DateTime(2026, 6, 6, 6, 0, 0, DateTimeKind.Utc), minutes: 240);

        var overtime = await OkAsync<MyOvertimeResponse>(Client.GetAsync("/api/me/overtime?month=2026-06", me.AccessToken));

        overtime.ShouldSatisfyAllConditions(
            o => o.InProgress.ShouldBeFalse(),
            o => o.OvertimeMinutes.ShouldBe(60),
            o => o.MadeUpMinutes.ShouldBe(60),
            o => o.PaidMinutes.ShouldBe(0),
            o => o.Entries.ShouldBe(1));
        var day = overtime.Days.ShouldHaveSingleItem();
        day.Date.ShouldBe(new DateOnly(2026, 6, 1));
        day.DayOff.ShouldBeFalse();
        day.Entries.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Minutes.ShouldBe(480),
            e => e.OvertimeMinutes.ShouldBe(60),
            e => e.MadeUpMinutes.ShouldBe(60),
            e => e.ProjectId.ShouldBe(project.Id));

        // The colleague's Saturday is a day off: all of it is overtime.
        var theirs = await OkAsync<MyOvertimeResponse>(Client.GetAsync("/api/me/overtime?month=2026-06", colleague.AccessToken));
        theirs.Days.Select(d => (d.Date.Day, d.DayOff)).ShouldBe([(6, true), (1, false)]);

        await (await Client.GetAsync("/api/me/overtime?month=2025-12", me.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.ValidationFailed);
        await (await Client.GetAsync("/api/me/overtime?month=2099-01", me.AccessToken)).ShouldFailWithAsync(400, ErrorCodes.SalaryMonthInFuture);
    }

    [Fact]
    public async Task Admins_have_no_hours_ledger()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);

        var hours = await OkAsync<MyHoursResponse>(Client.GetAsync("/api/me/hours", admin.AccessToken));

        hours.ShouldSatisfyAllConditions(h => h.Applies.ShouldBeFalse(), h => h.Months.ShouldBeEmpty());
    }

    [Fact]
    public async Task Execution_staff_do_not_reach_the_self_service_views()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, siteStaff) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);

        foreach (var url in new[] { "/api/me/schedule", "/api/me/hours", "/api/me/overtime" })
        {
            await (await Client.GetAsync(url, siteStaff.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        }
    }

    /// <summary>
    /// A Design employee who joined on 1 January 2026, with one 8-hour day on Monday 1 June (7h inside their
    /// hours, 1h beyond what the day requires) and an unpaid day off on Tuesday 2 June.
    /// </summary>
    private async Task<AuthResponse> JuneMonthAsync(AuthResponse superAdmin, long projectId, string basis, decimal amount)
    {
        var (_, _, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await ExecuteAsync("UPDATE users SET joined_on = '2026-01-01' WHERE id = @id", ("@id", person.User.Id));
        await OkAsync<object>(Client.PostJsonAsync($"/api/employees/{person.User.Id}/pay",
            new { basis, amount, currency = "USD", effectiveFrom = "2026-01-01" }, superAdmin.AccessToken), status: null);
        await AddWorkAsync(person, projectId, new DateTime(2026, 6, 1, 6, 0, 0, DateTimeKind.Utc), minutes: 480);
        await OkAsync<object>(Client.PostJsonAsync($"/api/employees/{person.User.Id}/hour-excuses",
            new { month = "2026-06", kind = "days", days = new[] { "2026-06-02" }, isPaid = false }, superAdmin.AccessToken), status: null);
        return person;
    }

    private static DateOnly TodayInBeirut() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Beirut));

    private async Task AddWorkAsync(AuthResponse person, long projectId, DateTime startUtc, int minutes)
        => await ExecuteAsync(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) VALUES (@u, @p, 'Drawings', @s, @e, UTC_TIMESTAMP(3))",
            ("@u", person.User.Id), ("@p", projectId), ("@s", startUtc), ("@e", startUtc.AddMinutes(minutes)));

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

    /// <param name="status">The status expected; null for any success.</param>
    private static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call, int? status = 200)
    {
        var response = await call;
        if (status is { } expected)
        {
            ((int)response.StatusCode).ShouldBe(expected, await response.Content.ReadAsStringAsync());
        }
        else
        {
            response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        }

        return await response.ReadAsync<T>();
    }
}
