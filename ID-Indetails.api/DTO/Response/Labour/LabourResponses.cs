using ID_Indetails.api.DTO.Response.Common;

namespace ID_Indetails.api.DTO.Response.Labour;

/// <param name="WorkerDays">Workers added up over the week's days.</param>
public sealed record LabourWeekSummaryResponse(
    DateOnly WeekStart,
    bool IsReported,
    decimal? Total,
    int? WorkerDays,
    PersonRefResponse? SubmittedBy,
    DateTime? SubmittedAt);

/// <param name="Weeks">Every week from the project's first to the current one (or the one it was finalised in), newest first.</param>
/// <param name="CanReport">Whether the caller may report weeks now.</param>
public sealed record LabourWeekListResponse(
    IReadOnlyList<LabourWeekSummaryResponse> Weeks,
    decimal Total,
    int ReportedWeeks,
    string Currency,
    bool CanReport);

public sealed record LabourDayResponse(DateOnly Date, int Workers, decimal DailyPay, decimal Cost);

public sealed record LabourWeekResponse
{
    public DateOnly WeekStart { get; init; }

    /// <summary>False for a week not yet reported: its days are filled in from the defaults, up to today.</summary>
    public bool IsReported { get; init; }

    public IReadOnlyList<LabourDayResponse> Days { get; init; } = [];

    public decimal Total { get; init; }

    public string Currency { get; init; } = "USD";

    public string? Note { get; init; }

    public PersonRefResponse? SubmittedBy { get; init; }

    public DateTime? SubmittedAt { get; init; }

    public DateTime? UpdatedAt { get; init; }

    /// <summary>Whether the caller may submit or change this week now.</summary>
    public bool CanEdit { get; init; }

    /// <summary>Opaque: "0" while the week isn't reported, otherwise a value that changes whenever the week changes.</summary>
    public string Version { get; init; } = "0";
}

public sealed record LabourDefaultResponse(
    DateOnly EffectiveFrom,
    int Workers,
    decimal DailyPay,
    string Currency,
    PersonRefResponse? SetBy,
    DateTime SetAt);

/// <param name="Current">The defaults in force today; null when none were ever set.</param>
/// <param name="History">Every change, newest first.</param>
public sealed record LabourDefaultsResponse(
    LabourDefaultResponse? Current,
    IReadOnlyList<LabourDefaultResponse> History,
    bool CanEdit);
