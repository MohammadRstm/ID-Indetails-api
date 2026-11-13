using ID_Indetails.api.DTO.Response.Insights;

namespace ID_Indetails.api.DTO.Response.Reports;

/// <summary>Hours only, in minutes. Never pay: no shortfall, made-up hours or overtime paid.</summary>
/// <param name="AvailableMinutes">Expected less excused: what there was to log.</param>
/// <param name="OvertimeMinutes">Time past each day's required hours, as employees see it.</param>
/// <param name="Utilization">Logged ÷ available, a fraction (0.92 is 92%; past 1 with overtime); null with nothing available.</param>
public sealed record UtilizationTotalsResponse(
    int LoggedMinutes,
    int ExpectedMinutes,
    int ExcusedMinutes,
    int AvailableMinutes,
    int OvertimeMinutes,
    decimal? Utilization);

/// <param name="Share">Its part of the person's logged minutes, a fraction.</param>
public sealed record UtilizationProjectResponse(long ProjectId, string Title, int Minutes, decimal Share);

/// <summary>One Design employee's hours in the month and where they went. Hours only.</summary>
public sealed record UtilizationPersonResponse(
    long UserId,
    string Name,
    string? Department,
    UtilizationTotalsResponse Hours,
    IReadOnlyList<UtilizationProjectResponse> Projects);

/// <param name="TeamId">Null for people in no department.</param>
public sealed record UtilizationDepartmentResponse(long? TeamId, string Name, int People, UtilizationTotalsResponse Hours);

/// <summary>
/// How much of their expected hours Design employees logged in a month, each person and each department, with each
/// person's project mix. Hours only; admins, super admins and site staff log no hours and aren't listed.
/// </summary>
/// <param name="Month">Its first day.</param>
/// <param name="InProgress">This month: it runs to today.</param>
public sealed record UtilizationResponse(
    DateOnly Month,
    bool InProgress,
    UtilizationTotalsResponse Total,
    IReadOnlyList<UtilizationDepartmentResponse> Departments,
    IReadOnlyList<UtilizationPersonResponse> People,
    InsightBasisResponse Basis);
