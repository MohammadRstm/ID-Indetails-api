using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.WorkEntries;
using MySqlConnector;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Past months never change: work that starts before the current company month is closed to every change.</summary>
public class ClosedMonthTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");

    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;
    private WorkEntryTestSteps Work => new(_steps);

    [Fact]
    public async Task Work_in_a_closed_month_can_t_be_logged_edited_moved_deleted_or_restored()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (_, _, admin) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Admin, DomainCodes.Design);
        var (_, _, designer) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var project = await new ProjectTestSteps(_steps).CreateProjectAsync(admin.AccessToken, DomainCodes.Design);

        // Future entries are allowed, so this month's entries can sit anywhere in it whatever the day.
        await Work.SetDesignSettingsAsync(admin.AccessToken, allowFutureEntries: true);
        var monthStart = CurrentMonthStartUtc();
        var lastMonth = monthStart.AddDays(-10);
        var closed = await InsertAsync(designer, project.Id, lastMonth, minutes: 120, deleted: false);
        var deletedClosed = await InsertAsync(designer, project.Id, lastMonth.AddHours(4), minutes: 60, deleted: true);

        await (await Work.TryLogAsync(designer.AccessToken, project.Id, lastMonth.AddHours(-5), 60))
            .ShouldFailWithAsync(422, ErrorCodes.MonthClosed);
        await (await Client.PutJsonAsync($"/api/work-entries/{closed}",
                new { projectId = project.Id, description = "Rework", startedAt = lastMonth, endedAt = lastMonth.AddHours(3) }, designer.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.MonthClosed);
        await (await Client.DeleteAsync($"/api/work-entries/{closed}", designer.AccessToken)).ShouldFailWithAsync(422, ErrorCodes.MonthClosed);
        await (await Client.PostEmptyAsync($"/api/work-entries/{deletedClosed}/restore", designer.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.MonthClosed);

        // This month's work can't be moved back into a closed month, and a closed entry can't be moved out of it
        // by the timeline's swap either.
        var current = await Work.LogAsync(designer.AccessToken, project.Id, monthStart.AddHours(12), 60);
        await (await Client.PutJsonAsync($"/api/work-entries/{current.Id}",
                new { projectId = project.Id, description = "Moved back", startedAt = lastMonth.AddHours(8), endedAt = lastMonth.AddHours(9) }, designer.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.MonthClosed);
        await (await Client.PostJsonAsync("/api/work-entries", new
            {
                projectId = project.Id,
                description = "Swap",
                startedAt = monthStart.AddHours(20),
                endedAt = monthStart.AddHours(21),
                moves = new[] { new { id = closed, startedAt = monthStart.AddHours(14), endedAt = monthStart.AddHours(16) } },
            }, designer.AccessToken))
            .ShouldFailWithAsync(422, ErrorCodes.MonthClosed);

        (await EntryAsync(closed, designer)).CanEdit.ShouldBeFalse();
        (await EntryAsync(current.Id, designer)).CanEdit.ShouldBeTrue();
        (await _steps.ScalarAsync<DateTime>("SELECT started_at FROM work_entries WHERE id = @id", ("@id", closed))).ShouldBe(lastMonth);
    }

    /// <summary>When the current month began in Beirut, in UTC.</summary>
    private static DateTime CurrentMonthStartUtc()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Beirut);
        return TimeZoneInfo.ConvertTimeToUtc(new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified), Beirut);
    }

    private async Task<WorkEntryResponse> EntryAsync(long id, AuthResponse person)
    {
        var response = await Client.GetAsync($"/api/work-entries/{id}", person.AccessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<WorkEntryDetailResponse>()).Entry;
    }

    /// <summary>An entry written straight to the database, as if it had been logged before the month closed.</summary>
    private async Task<long> InsertAsync(AuthResponse person, long projectId, DateTime startUtc, int minutes, bool deleted)
    {
        await using var connection = await api.Database.OpenConnectionAsync();
        await using var command = new MySqlCommand(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at, deleted_at) " +
            "VALUES (@u, @p, 'Drawings', @s, @e, UTC_TIMESTAMP(3), @d); SELECT LAST_INSERT_ID();",
            connection);
        command.Parameters.AddWithValue("@u", person.User.Id);
        command.Parameters.AddWithValue("@p", projectId);
        command.Parameters.AddWithValue("@s", startUtc);
        command.Parameters.AddWithValue("@e", startUtc.AddMinutes(minutes));
        command.Parameters.AddWithValue("@d", deleted ? DateTime.UtcNow : DBNull.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
