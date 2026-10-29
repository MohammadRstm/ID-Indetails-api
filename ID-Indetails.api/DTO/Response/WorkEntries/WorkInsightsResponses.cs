using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.WorkEntries;

public sealed record CurrencyAmountResponse(string Currency, decimal Amount);

/// <summary>
/// Labour cost priced by the shared finance calculator: each hour at the person's hourly cost in force that day,
/// converted to USD at that day's exchange rate.
/// </summary>
/// <param name="Unconverted">Cost with no exchange rate that day, left in its own currency.</param>
/// <param name="UnpricedMinutes">Time of people with no pay record, which can't be priced.</param>
public sealed record LabourCostResponse(decimal Usd, IReadOnlyList<CurrencyAmountResponse> Unconverted, int UnpricedMinutes);

public sealed record HourCellResponse(DayOfWeek Weekday, int Hour, int Minutes);

/// <param name="OvertimeMinutes">The part of <paramref name="Minutes"/> that is overtime; the rest is normal time.</param>
/// <param name="Weekly">The last 8 weeks, oldest first.</param>
/// <param name="LabourCost">Only for callers with financial:view.</param>
public sealed record ProjectContributorResponse(
    long UserId,
    string Name,
    int Entries,
    int Minutes,
    int OvertimeMinutes,
    int SharePct,
    DateTime LastStartedAt,
    IReadOnlyList<int> Weekly,
    LabourCostResponse? LabourCost,
    int OutsideScopeMinutes = 0);

/// <summary>A Design project's logged work, for the admin project page.</summary>
/// <param name="TotalOvertimeMinutes">The part of <paramref name="TotalMinutes"/> that is overtime; the rest is normal time.</param>
/// <param name="ActivityLevel">From the last 14 days: busy from 45 h a week, steady from 15 h, quiet with any work.</param>
/// <param name="Weekly">Weekly hours from the project's start (4 to 30 weeks), oldest first.</param>
/// <param name="Contributors">Within the requested range and work text, most time first.</param>
/// <param name="OutsideScopeMinutes">The part of <paramref name="TotalMinutes"/> logged as outside the project's original scope.</param>
public sealed record ProjectWorkResponse(
    long ProjectId,
    int TotalMinutes,
    int TotalOvertimeMinutes,
    int EntryCount,
    int ContributorCount,
    DateTime? LastStartedAt,
    ActivityLevel ActivityLevel,
    int MinutesLast14Days,
    int MinutesPrevious14Days,
    IReadOnlyList<WeekTotalResponse> Weekly,
    IReadOnlyList<HourCellResponse> HourHeat,
    IReadOnlyList<ProjectContributorResponse> Contributors,
    int OutsideScopeMinutes = 0);

/// <summary>One person's work on a project, within the requested range and work text.</summary>
/// <param name="OvertimeMinutes">The part of <paramref name="Minutes"/> that is overtime; the rest is normal time.</param>
/// <param name="SharePct">Of the project's minutes, all of them.</param>
/// <param name="Weekly">The last 10 weeks, oldest first.</param>
/// <param name="RecentEntries">The 40 latest entries, newest first.</param>
public sealed record ProjectContributorDetailResponse(
    long ProjectId,
    WorkEntryPersonResponse Person,
    int Minutes,
    int OvertimeMinutes,
    int Entries,
    int SharePct,
    IReadOnlyList<WeekTotalResponse> Weekly,
    IReadOnlyList<WorkEntryResponse> RecentEntries);

public sealed record FocusProjectResponse(long ProjectId, string Title);

/// <param name="Focus">The project with the most time in the last 14 days.</param>
/// <param name="Weekly">The last 8 weeks, oldest first.</param>
/// <param name="LabourCost">All time; only for callers with financial:view.</param>
/// <param name="MinutesLast30Days">Logged over the last 30 days, today included: the page adds them up per team.</param>
/// <param name="OvertimeMinutesThisMonth">This month's overtime, from each whole day (see IOvertimeService).</param>
/// <param name="HourlyRate">Their hourly cost today, only for callers with financial:view.</param>
public sealed record PersonWorkResponse(
    long Id,
    string Name,
    string Email,
    string? Position,
    string? TeamName,
    string Role,
    UserStatus Status,
    long? PhotoFileId,
    bool Online,
    DateTime? LastSeenAt,
    int WeekMinutes,
    int QuarterMinutes,
    int TotalMinutes,
    int Entries,
    DateTime? LastStartedAt,
    FocusProjectResponse? Focus,
    IReadOnlyList<int> Weekly,
    int MinutesLast30Days,
    int OvertimeMinutesThisMonth,
    CurrencyAmountResponse? HourlyRate,
    LabourCostResponse? LabourCost);

public sealed record TimelinePersonResponse(long UserId, string Name, int Minutes);

public sealed record TimelineMeetingResponse(long Id, string? Topic, DateTime StartsAt, DateTime EndsAt);

/// <summary>A company-time-zone day with work or a meeting. Work past midnight counts on each day it covers.</summary>
public sealed record TimelineDayResponse(
    DateOnly Day,
    int Minutes,
    IReadOnlyList<TimelinePersonResponse> People,
    IReadOnlyList<TimelineMeetingResponse> Meetings);

/// <param name="Amount">The new progress for progress_set; null otherwise.</param>
public sealed record TimelineEventResponse(string Kind, DateTime OccurredAt, string? ActorName, decimal? Amount);

public sealed record TimelineGapResponse(DateOnly From, DateOnly To, int Days);

/// <summary>A Design project's history, day by day, for the admin Timeline tab.</summary>
/// <param name="Events">Status changes and progress, oldest first.</param>
/// <param name="Gaps">Runs of 4 days or more without work, between the start and the end.</param>
public sealed record ProjectTimelineResponse(
    long ProjectId,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<TimelineDayResponse> Days,
    IReadOnlyList<TimelineEventResponse> Events,
    IReadOnlyList<TimelineGapResponse> Gaps);

public sealed record ProjectLabourCostResponse(long ProjectId, string Title, string? Icon, string? Tint, LabourCostResponse Cost);

/// <summary>What someone's logged work cost the company (their profile's "cost to the company").</summary>
/// <param name="ThisQuarter">From the first of this quarter to today.</param>
/// <param name="ByProject">All time: the 8 projects their work cost most on, in USD, most first.</param>
/// <param name="HourlyRate">What an hour of their time costs this month; null without pay.</param>
public sealed record EmployeeLabourCostResponse(
    long UserId,
    int EntryCount,
    LabourCostResponse ThisQuarter,
    LabourCostResponse AllTime,
    IReadOnlyList<ProjectLabourCostResponse> ByProject,
    CurrencyAmountResponse? HourlyRate);

/// <param name="Minutes">Hours logged in the week.</param>
/// <param name="Labour">Employee labour cost of the week (paid breaks included).</param>
/// <param name="Spent">Every cost of the week: labour, expenses, site expenses and ground labour.</param>
/// <param name="CostToDate">Every cost up to the end of the week.</param>
/// <param name="CollectedToDate">Payments received up to the end of the week.</param>
public sealed record CostWeekResponse(DateOnly WeekStart, int Minutes, decimal Labour, decimal Spent, decimal CostToDate, decimal CollectedToDate);

/// <summary>A project's cost and payments week by week, from its start to today (or when it was finalised), in USD.</summary>
public sealed record ProjectCostTimelineResponse(IReadOnlyList<CostWeekResponse> Weeks, string Currency);

/// <summary>One entry on the Timeline tab's lanes and one-day view.</summary>
/// <param name="TeamName">For grouping the day by team; null for someone in no team.</param>
public sealed record TimelineEntryResponse(
    long Id,
    long UserId,
    string UserName,
    string? TeamName,
    DateTime StartedAt,
    DateTime EndedAt,
    int Minutes,
    string Description);
