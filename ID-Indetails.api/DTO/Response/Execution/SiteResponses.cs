using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Execution;

/// <summary>A site's card: what was recorded there, and when the caller last recorded something.</summary>
/// <param name="MaterialCost">From the shared finance calculator, in USD.</param>
/// <param name="GroundLabour">From the shared finance calculator, in USD.</param>
public sealed record SiteSummaryResponse(
    long ProjectId,
    int MaterialCount,
    decimal MaterialCost,
    int ReportedWeeks,
    decimal GroundLabour,
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

    /// <summary>The latest records, newest first.</summary>
    public IReadOnlyList<SiteRecordResponse> Recent { get; init; } = [];
}
