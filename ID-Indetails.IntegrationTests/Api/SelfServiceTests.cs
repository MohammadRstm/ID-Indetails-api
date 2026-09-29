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
    public async Task Execution_staff_do_not_reach_the_self_service_views()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, siteStaff) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Execution);

        foreach (var url in new[] { "/api/me/schedule" })
        {
            await (await Client.GetAsync(url, siteStaff.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
        }
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

    private static async Task<T> OkAsync<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }
}
