namespace ID_Indetails.api.DTO.Response.Reports;

/// <summary>The Design employees who have logged no work this week, as the dashboard's "not logging" item counts them.</summary>
/// <param name="WeekStart">The first day of this week, in the company calendar.</param>
/// <param name="Today">The last day of the week so far.</param>
/// <param name="People">By name.</param>
public sealed record NotLoggingResponse(DateOnly WeekStart, DateOnly Today, IReadOnlyList<NotLoggingPersonResponse> People);

/// <summary>
/// One person who has logged no work this week. The three minutes are colleagues' hours: null unless the caller holds
/// <c>work_entry:view_all</c>.
/// </summary>
/// <param name="LoggedMinutes">Logged this week so far, as the hours ledger counts them.</param>
/// <param name="ExpectedMinutes">What there was to log on their working days this week so far, less excused days.</param>
/// <param name="OvertimeMinutes">The overtime of what they logged this week.</param>
/// <param name="RemindedToday">Someone already asked them today to log their hours (one reminder a day, from anyone).</param>
public sealed record NotLoggingPersonResponse(
    long Id,
    string Name,
    string Email,
    long? PhotoFileId,
    int? LoggedMinutes,
    int? ExpectedMinutes,
    int? OvertimeMinutes,
    bool RemindedToday);

/// <param name="Reminded">How many were sent a reminder now.</param>
/// <param name="Skipped">How many were left out because they were already reminded today.</param>
public sealed record HoursRemindersResponse(int Reminded, int Skipped);
