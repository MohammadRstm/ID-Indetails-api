using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Business.Labour;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Execution;

/// <summary>A site's card: what was recorded there, and when the caller last recorded something.</summary>
/// <param name="MaterialCost">From the shared finance calculator, in USD; null without financial:view.</param>
/// <param name="GroundLabour">From the shared finance calculator, in USD; null without financial:view.</param>
public sealed record SiteSummaryResponse(
    long ProjectId,
    int MaterialCount,
    decimal? MaterialCost,
    int ReportedWeeks,
    decimal? GroundLabour,
    DateTime? MyLastRecordedAt);

public sealed record SiteRecordsWeekResponse(DateOnly WeekStart, decimal Materials, decimal Labour, decimal Value);

public sealed record SiteRecordsProjectResponse(
    long ProjectId,
    string Title,
    ProjectStatus Status,
    int MaterialCount,
    decimal Materials,
    int LabourWeeks,
    decimal Labour,
    DateTime LastRecordedAt);

/// <param name="Label">The material's type, or the Monday of the labour week.</param>
public sealed record SiteRecordResponse(SiteRecords.Kind Kind, long Id, long ProjectId, string ProjectTitle, DateTime At, string Label, decimal Amount);

/// <summary>
/// What one person recorded on sites: materials by when they were added, labour weeks by when they
/// were submitted. Only sites the caller can see are counted.
/// </summary>
public sealed record SiteRecordsResponse
{
    public PersonRefResponse Person { get; init; } = null!;

    public string Currency { get; init; } = "USD";

    public int MaterialCount { get; init; }

    public decimal Materials { get; init; }

    public int LabourWeeks { get; init; }

    public decimal Labour { get; init; }

    public decimal Total { get; init; }

    public decimal ThisQuarter { get; init; }

    /// <summary>Days with anything recorded.</summary>
    public int ActiveDays { get; init; }

    /// <summary>The last 16 weeks, oldest first, ending with the current one.</summary>
    public IReadOnlyList<SiteRecordsWeekResponse> Weeks { get; init; } = [];

    /// <summary>Most recently recorded first.</summary>
    public IReadOnlyList<SiteRecordsProjectResponse> Projects { get; init; } = [];

    /// <summary>A page of the records, newest first: the page and its size the query asked for.</summary>
    public IReadOnlyList<SiteRecordResponse> Recent { get; init; } = [];

    /// <summary>How many records there are in all, to page <see cref="Recent"/>.</summary>
    public int RecentTotal { get; init; }
}

/// <summary>Where cash stood at the end of a week, in USD. <c>Received</c>, <c>Spent</c> and <c>GroundLabour</c> add up from the start.</summary>
public sealed record CashWeekResponse(
    DateOnly WeekStart,
    decimal Received,
    decimal Spent,
    decimal GroundLabour,
    decimal Profit,
    decimal ReceivedInWeek,
    decimal GroundLabourInWeek);

/// <summary>
/// When cash profit would reach zero if the crew, their pay and the money received stayed as they are
/// today. <c>ZeroOn</c> is set only when <c>State</c> is <c>ok</c>.
/// </summary>
public sealed record SiteProjectionResponse(
    LabourRules.ProjectionState State,
    int Workers,
    decimal DailyPay,
    decimal DailyCost,
    decimal CashProfit,
    decimal ExpectedProfit,
    DateOnly? ZeroOn,
    int? WorkingDays,
    DateOnly? ZeroOnOncePaid,
    int? WorkingDaysOncePaid);

/// <param name="Weeks">From the project's first week to the current one (or the one it was finalised in), oldest first.</param>
public sealed record SiteMoneyTimelineResponse(IReadOnlyList<CashWeekResponse> Weeks, SiteProjectionResponse Projection, string Currency);

public sealed record SiteLogProjectResponse(long Id, string Title);

/// <param name="CanEdit">The caller wrote it, today (company time): a log is only changed on the day it was written.</param>
public sealed record SiteLogResponse(
    long Id,
    SiteLogProjectResponse Project,
    PersonRefResponse Person,
    string Body,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    bool CanEdit);

/// <summary>A site the caller oversees.</summary>
public sealed record MySiteResponse(long Id, string Title, string? ClientName, ProjectStatus Status, string? Location, DateTime AssignedAt);
