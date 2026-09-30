using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Reports;

public sealed record ReportProjectResponse(
    long ProjectId,
    string Title,
    ProjectStatus Status,
    int Minutes,
    int Entries,
    int People,
    int SharePct,
    IReadOnlyList<int> Weekly);

/// <param name="Name">Null for internal work (no client).</param>
public sealed record ReportClientResponse(long? ClientId, string? Name, int Minutes);

/// <param name="OvertimeMinutes">The part of <paramref name="Minutes"/> that is overtime; the rest is normal time.</param>
public sealed record ReportPersonResponse(long UserId, string Name, int Minutes, int OvertimeMinutes, int Entries);

public sealed record ReportMatrixProjectResponse(long ProjectId, string Title);

/// <param name="OvertimeMinutes">The part of <paramref name="TotalMinutes"/> that is overtime, on every project.</param>
/// <param name="Cells">Minutes on each of the matrix's projects, in the same order.</param>
public sealed record ReportMatrixRowResponse(long UserId, string Name, int TotalMinutes, int OvertimeMinutes, IReadOnlyList<int> Cells);

/// <summary>People against the range's six busiest projects.</summary>
public sealed record ReportMatrixResponse(IReadOnlyList<ReportMatrixProjectResponse> Projects, IReadOnlyList<ReportMatrixRowResponse> Rows);

/// <param name="Minutes">All the project's work, not only the range's.</param>
public sealed record CompletedProjectResponse(long ProjectId, string Title, DateOnly StartDate, DateOnly CompletedOn, int Days, int Minutes, int People);

public sealed record QuarterTotalResponse(DateOnly QuarterStart, int Minutes);

public sealed record ProjectProfitResponse(long ProjectId, string Title, decimal Revenue, decimal Cost, decimal Profit);

public sealed record PersonLabourCostResponse(long UserId, string Name, LabourCostResponse LabourCost);

/// <summary>Money over the range, in USD. Only for callers with financial:view.</summary>
/// <param name="Expenses">Expenses of every Design project dated in the range, whatever the project and person filters.</param>
/// <param name="ClientProfit">All-time profit of every Design client project.</param>
/// <param name="ProfitByProject">All-time figures of the projects worked on in the range.</param>
public sealed record ReportMoneyResponse(
    LabourCostResponse Labour,
    decimal ExpensesUsd,
    IReadOnlyList<CurrencyAmountResponse> UnconvertedExpenses,
    decimal TotalCostUsd,
    decimal ClientProfit,
    IReadOnlyList<ProjectProfitResponse> ProfitByProject,
    IReadOnlyList<PersonLabourCostResponse> LabourByPerson);

/// <summary>Design work over a range of company days, both inclusive.</summary>
/// <param name="OvertimeMinutes">The part of <paramref name="TotalMinutes"/> that is overtime; the rest is normal time.</param>
/// <param name="Weekly">3 to 20 weeks ending with the range, oldest first.</param>
/// <param name="CompletedProjects">Design projects completed in the range, whatever the filters.</param>
/// <param name="Quarterly">The last 6 quarters of all Design work, whatever the range and filters, oldest first.</param>
public sealed record WorkReportResponse(
    DateOnly From,
    DateOnly To,
    int TotalMinutes,
    int OvertimeMinutes,
    int EntryCount,
    int ProjectCount,
    int PeopleCount,
    int AverageEntryMinutes,
    WeekTotalResponse? BusiestWeek,
    IReadOnlyList<WeekTotalResponse> Weekly,
    IReadOnlyList<ReportProjectResponse> ByProject,
    IReadOnlyList<ReportClientResponse> ByClient,
    IReadOnlyList<ReportPersonResponse> ByPerson,
    ReportMatrixResponse Matrix,
    IReadOnlyList<CompletedProjectResponse> CompletedProjects,
    IReadOnlyList<QuarterTotalResponse> Quarterly,
    ReportMoneyResponse? Money);

/// <param name="Days">Idle: days since the last work; on hold: days since it was put on hold.</param>
/// <param name="Count">Not logging: how many people.</param>
/// <param name="People">Not logging: the first 3 of them, A to Z.</param>
public sealed record AttentionItemResponse(
    AttentionKind Kind,
    long? ProjectId,
    string? Title,
    int? Days,
    int? ProgressPct,
    int? Count,
    IReadOnlyList<PersonRefResponse>? People = null);

public sealed record StatusCountResponse(ProjectStatus Status, int Count);

public sealed record ProjectMinutesResponse(long ProjectId, string Title, int Minutes);

/// <summary>The Design dashboard for a period and the one before it.</summary>
/// <param name="Weekly">The period's weeks, oldest first.</param>
/// <param name="PreviousWeekly">As many weeks ending with the previous period, oldest first, for comparison.</param>
/// <param name="OvertimeMinutes">The part of the period's <paramref name="Minutes"/> that is overtime.</param>
/// <param name="Attention">At most 5 items.</param>
public sealed record OverviewResponse(
    OverviewPeriod Period,
    DateOnly From,
    DateOnly To,
    DateOnly PreviousFrom,
    DateOnly PreviousTo,
    int Minutes,
    int OvertimeMinutes,
    int PreviousMinutes,
    int Entries,
    int PreviousEntries,
    int AverageEntryMinutes,
    int PeopleLoggingThisWeek,
    int People,
    IReadOnlyList<WeekTotalResponse> Weekly,
    IReadOnlyList<WeekTotalResponse> PreviousWeekly,
    IReadOnlyList<AttentionItemResponse> Attention,
    IReadOnlyList<ProjectMinutesResponse> ByProject,
    IReadOnlyList<StatusCountResponse> ProjectsByStatus,
    ProjectMinutesResponse? BusiestProjectThisWeek);
