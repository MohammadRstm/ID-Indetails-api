using ID_Indetails.api.DTO.Response.WorkEntries;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Reusable steps for tests that log Design work.</summary>
public sealed class WorkEntryTestSteps(AuthTestSteps steps)
{
    private HttpClient Client => steps.Client;

    /// <summary>A whole minute a few hours ago, so entries starting there end in the past.</summary>
    public static DateTime HoursAgo(int hours)
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).AddHours(-hours);
    }

    /// <summary>The company day (Asia/Beirut) an instant falls on.</summary>
    public static DateOnly CompanyDay(DateTime utc)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "Asia/Beirut"));

    /// <summary>
    /// Work at fixed UTC times, written straight to the database, so money figures (the paid break depends on the
    /// weekday and the hours) don't depend on when the test runs.
    /// </summary>
    public Task InsertAsync(long userId, long projectId, string startUtc, string endUtc, string description = "Drawings")
        => steps.ScalarAsync<int>(
            "INSERT INTO work_entries (user_id, project_id, description, started_at, ended_at, created_at) " +
            "VALUES (@u, @p, @d, @s, @e, UTC_TIMESTAMP(3)); SELECT 1",
            ("@u", userId), ("@p", projectId), ("@d", description), ("@s", startUtc), ("@e", endUtc));

    public Task<HttpResponseMessage> TryLogAsync(string accessToken, long projectId, DateTime start, int minutes, string description = "Layout work")
        => Client.PostJsonAsync("/api/work-entries", new { projectId, description, startedAt = start, endedAt = start.AddMinutes(minutes) }, accessToken);

    public async Task<WorkEntryResponse> LogAsync(string accessToken, long projectId, DateTime start, int minutes, string description = "Layout work")
    {
        var response = await TryLogAsync(accessToken, projectId, start, minutes, description);
        ((int)response.StatusCode).ShouldBe(201, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<WorkEntryResponse>();
    }

    /// <summary>Replaces the Design settings; anything not given is the default.</summary>
    public async Task SetDesignSettingsAsync(
        string accessToken,
        string editWindow = "one_day",
        bool allowOverlap = false,
        bool allowFutureEntries = false,
        bool requireDescription = true,
        string notifyEntry = "off",
        bool holdLogging = false,
        bool autoArchive = false)
    {
        var response = await Client.PutJsonAsync("/api/domain-settings/design", new
        {
            editWindow,
            allowOverlap,
            allowFutureEntries,
            requireDescription,
            timerEnabled = true,
            logReminder = true,
            notifyEntry,
            notifyProject = true,
            notifyIdle = true,
            weeklySummary = true,
            holdLogging,
            autoArchive,
        }, accessToken);
        ((int)response.StatusCode).ShouldBe(200, await response.Content.ReadAsStringAsync());
    }
}
