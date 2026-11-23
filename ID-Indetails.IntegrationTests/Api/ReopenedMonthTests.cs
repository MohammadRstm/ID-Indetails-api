using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// A super admin reopens a closed month for logging (23 November 2026). The tests share one database and reopened months
/// are company-wide, so these use February 2025, which no other test logs in, and lock it again when they finish.
/// </summary>
public class ReopenedMonthTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Month = "2025-02";

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private WorkEntryTestSteps Work => new(_steps);

    // Beirut is UTC+2 in February: 07:00 UTC is 09:00, the start of the working day
    private static DateTime On(int day, int hourUtc = 7) => new(2025, 2, day, hourUtc, 0, 0, DateTimeKind.Utc);

    private async Task<(ID_Indetails.api.DTO.Response.Auth.AuthResponse SuperAdmin, ID_Indetails.api.DTO.Response.Auth.AuthResponse Designer, long ProjectId)> SetUpAsync()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        await _steps.SetJoinedOnAsync(designer.User.Id, "2025-01-01");
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(superAdmin.AccessToken, DomainCodes.Design);
        return (superAdmin, designer, project.Id);
    }

    private Task<HttpResponseMessage> ReopenAsync(string token, string month = Month, DateOnly? until = null)
        => Client.PutJsonAsync($"/api/reopened-months/{month}",
            new { openUntil = (until ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7)).ToString("yyyy-MM-dd"), note = "Catching up on hours" }, token);

    private Task<long> NotificationsAsync(long recipientId, string kind)
        => _steps.ScalarAsync<long>("SELECT COUNT(*) FROM notifications WHERE recipient_id = @r AND kind = @k", ("@r", recipientId), ("@k", kind));

    [Fact]
    public async Task A_super_admin_reopens_a_closed_month_and_people_log_in_it_until_it_locks_again()
    {
        var (superAdmin, designer, projectId) = await SetUpAsync();
        try
        {
            await (await Work.TryLogAsync(designer.AccessToken, projectId, On(4), 60)).ShouldFailWithAsync(422, ErrorCodes.MonthClosed);

            // only the super admins reopen a month, only a closed one, and until today or later
            await (await ReopenAsync(designer.AccessToken)).ShouldFailWithAsync(403, ErrorCodes.Forbidden);
            await (await ReopenAsync(superAdmin.AccessToken, DateTime.UtcNow.AddMonths(2).ToString("yyyy-MM"))).ShouldFailWithAsync(400, ErrorCodes.MonthNotClosed);
            await (await ReopenAsync(superAdmin.AccessToken, until: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2)))
                .ShouldFailWithAsync(400, ErrorCodes.ReopenUntilInPast);

            var reopened = await ReopenAsync(superAdmin.AccessToken);
            reopened.IsSuccessStatusCode.ShouldBeTrue(await reopened.Content.ReadAsStringAsync());
            (await (await Client.GetAsync("/api/reopened-months", designer.AccessToken)).ReadAsync<List<ReopenedMonthResponse>>())
                .ShouldContain(m => m.Month == Month && m.Note == "Catching up on hours");
            (await NotificationsAsync(designer.User.Id, NotificationKinds.MonthReopened)).ShouldBeGreaterThan(0);

            // open: logged, changed and removed as in the current month
            var logged = await Work.LogAsync(designer.AccessToken, projectId, On(4), 120);
            logged.CanEdit.ShouldBeTrue();
            (await Client.DeleteAsync($"/api/work-entries/{logged.Id}", designer.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
            var kept = await Work.LogAsync(designer.AccessToken, projectId, On(5), 120);

            // locked again: nothing more changes
            (await Client.DeleteAsync($"/api/reopened-months/{Month}", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
            await (await Work.TryLogAsync(designer.AccessToken, projectId, On(6), 60)).ShouldFailWithAsync(422, ErrorCodes.MonthClosed);
            await (await Client.DeleteAsync($"/api/work-entries/{kept.Id}", designer.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.MonthClosed);
            (await (await Client.GetAsync("/api/reopened-months", designer.AccessToken)).ReadAsync<List<ReopenedMonthResponse>>())
                .ShouldNotContain(m => m.Month == Month);
        }
        finally
        {
            await Client.DeleteAsync($"/api/reopened-months/{Month}", superAdmin.AccessToken);
        }
    }

    [Fact]
    public async Task Logging_on_an_excused_day_takes_that_day_off_the_excuse_and_tells_whoever_gave_it()
    {
        var (superAdmin, designer, projectId) = await SetUpAsync();
        try
        {
            // Tuesday 11 and Wednesday 12 February excused; 120 minutes excused in the month too
            (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/hour-excuses",
                new { month = Month, kind = "days", days = new[] { "2025-02-11", "2025-02-12" }, isPaid = true }, superAdmin.AccessToken))
                .IsSuccessStatusCode.ShouldBeTrue();
            (await Client.PostJsonAsync($"/api/employees/{designer.User.Id}/hour-excuses",
                new { month = Month, kind = "minutes", minutes = 120, isPaid = true }, superAdmin.AccessToken))
                .IsSuccessStatusCode.ShouldBeTrue();
            (await ReopenAsync(superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

            // the log window warns before they log
            var day = await (await Client.GetAsync("/api/me/day?date=2025-02-11", designer.AccessToken)).ReadAsync<MyDayResponse>();
            day.Excuse.ShouldNotBeNull();
            day.Excuse.Day.ShouldBeTrue();
            day.Excuse.MonthMinutes.ShouldBe(120);

            await Work.LogAsync(designer.AccessToken, projectId, On(11), 180);

            // the day excuse lost the 11th and kept the 12th, in a new excuse; the old one stays in history
            (await _steps.ScalarAsync<long>(
                    "SELECT COUNT(*) FROM hour_excuses WHERE user_id = @u AND kind = 'days' AND deleted_at IS NOT NULL", ("@u", designer.User.Id)))
                .ShouldBe(1);
            (await _steps.ScalarAsync<string>(
                    "SELECT CAST(d.day AS CHAR) FROM hour_excuse_days d JOIN hour_excuses x ON x.id = d.excuse_id WHERE x.user_id = @u AND x.deleted_at IS NULL",
                    ("@u", designer.User.Id)))
                .ShouldBe("2025-02-12");
            (await NotificationsAsync(superAdmin.User.Id, NotificationKinds.HoursExcuseReduced)).ShouldBeGreaterThan(0);

            // the month still needs far more than three hours, so the minutes excuse stands
            (await _steps.ScalarAsync<long>(
                    "SELECT COUNT(*) FROM hour_excuses WHERE user_id = @u AND kind = 'minutes' AND deleted_at IS NULL AND minutes = 120", ("@u", designer.User.Id)))
                .ShouldBe(1);
        }
        finally
        {
            await Client.DeleteAsync($"/api/reopened-months/{Month}", superAdmin.AccessToken);
        }
    }
}
