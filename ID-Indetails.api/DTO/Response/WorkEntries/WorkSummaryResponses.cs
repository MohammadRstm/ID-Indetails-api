using ID_Indetails.api.DTO.Response.Meetings;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.WorkEntries;

public sealed record WeekTotalResponse(DateOnly WeekStart, int Minutes, int Entries);

public sealed record DayTotalResponse(DateOnly Day, int Minutes);

/// <param name="OvertimeMinutes">The part of <paramref name="Minutes"/> that is overtime; the rest is normal time.</param>
public sealed record ProjectTimeResponse(
    long ProjectId,
    string Title,
    string? Icon,
    string? Tint,
    ProjectStatus Status,
    int Minutes,
    int OvertimeMinutes,
    int Entries,
    DateTime LastStartedAt);

/// <summary>
/// One person's work, as their "My work" page and their profile show it. Days are company-time-zone days;
/// weeks start on the company's week start.
/// </summary>
/// <param name="Week">Each day of the current week, first day first.</param>
/// <param name="LastWeekMinutes">The whole of last week.</param>
/// <param name="LastQuarterSameSpanMinutes">Last quarter, up to the same number of days as this quarter so far.</param>
/// <param name="TotalOvertimeMinutes">The part of <paramref name="TotalMinutes"/> that is overtime; the rest is normal time.</param>
/// <param name="Weekly">The last 16 weeks, oldest first.</param>
/// <param name="Daily">Days with work in the last 365 days, oldest first.</param>
/// <param name="ByProject">Time per project within the requested range and filters, most first.</param>
public sealed record WorkSummaryResponse(
    WorkEntryPersonResponse Person,
    DateOnly Today,
    DayOfWeek FirstDayOfWeek,
    int TodayMinutes,
    IReadOnlyList<DayTotalResponse> Week,
    int ThisWeekMinutes,
    int LastWeekMinutes,
    int ThisMonthMinutes,
    int ThisQuarterMinutes,
    int LastQuarterSameSpanMinutes,
    int TotalMinutes,
    int TotalOvertimeMinutes,
    int EntryCount,
    DateOnly? FirstEntryDay,
    int ProjectCount,
    int ActiveDaysLastYear,
    int LongestStreakLastYear,
    IReadOnlyList<WeekTotalResponse> Weekly,
    IReadOnlyList<DayTotalResponse> Daily,
    IReadOnlyList<ProjectTimeResponse> ByProject);

public sealed record ContributorResponse(long Id, string Name);

public sealed record LastActivityResponse(long PersonId, string PersonName, DateTime StartedAt);

/// <summary>An open Design project with the caller's own time on it. Other people's hours stay private.</summary>
public sealed record MyProjectResponse(
    long Id,
    string Title,
    string? Description,
    string? Icon,
    string? Tint,
    string? ClientName,
    ProjectStatus Status,
    int ProgressPct,
    DateOnly StartDate,
    DateTime CreatedAt,
    bool AcceptsWork,
    int MyMinutes,
    DateTime? MyLastStartedAt,
    IReadOnlyList<ContributorResponse> Contributors,
    LastActivityResponse? LastActivity);

public sealed record TeamActivityResponse(long PersonId, string PersonName, DateTime StartedAt, string Description);

/// <param name="MyOvertimeMinutes">The part of <paramref name="MyMinutes"/> that is overtime; the rest is normal time.</param>
/// <param name="Recent">The caller's 12 latest entries on the project, newest first.</param>
/// <param name="Weekly">The caller's last 8 weeks on the project, oldest first.</param>
/// <param name="Team">The 6 latest entries by other people, newest first.</param>
public sealed record MyProjectWorkResponse(
    long ProjectId,
    bool AcceptsWork,
    int MyMinutes,
    int MyOvertimeMinutes,
    int MyEntries,
    int MyThisWeekMinutes,
    int ContributorCount,
    int TotalMinutes,
    IReadOnlyList<WorkEntryResponse> Recent,
    IReadOnlyList<WeekTotalResponse> Weekly,
    IReadOnlyList<TeamActivityResponse> Team);

/// <summary>One company-time-zone day: the caller's entries and the domain's meetings, earliest first. Meetings are not work time.</summary>
/// <param name="OvertimeMinutes">The part of <paramref name="LoggedMinutes"/> that is overtime; the rest is normal time.</param>
public sealed record MyDayResponse(
    DateOnly Date,
    int LoggedMinutes,
    int OvertimeMinutes,
    IReadOnlyList<WorkEntryResponse> Entries,
    IReadOnlyList<MeetingResponse> Meetings);
