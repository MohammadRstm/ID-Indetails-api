using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Insights;

/// <summary>Labour cost and hours over a span, in USD at the rate of each day, paid breaks included.</summary>
/// <param name="OutsideScopeCost">The cost of the hours logged outside the agreed scope (their worked time; paid breaks stay with the day).</param>
/// <param name="OutsideScopeShare">Outside-scope cost ÷ cost, a fraction (0.12 is 12%); null with no cost.</param>
public sealed record LabourTotalsResponse(decimal Cost, int Minutes, decimal OutsideScopeCost, int OutsideScopeMinutes, decimal? OutsideScopeShare);

public sealed record LabourMonthResponse(DateOnly Month, LabourTotalsResponse Totals);

/// <param name="LabourShare">Its part of the span's labour cost, a fraction; null with no cost.</param>
public sealed record LabourProjectResponse(long ProjectId, string Title, BillingType BillingType, LabourTotalsResponse Totals, decimal? LabourShare);

/// <summary>A department's labour, never its people.</summary>
/// <param name="TeamId">Null for hours logged by people in no department.</param>
/// <param name="LabourShare">Its part of the span's labour cost, a fraction; null with no cost.</param>
public sealed record LabourDepartmentResponse(long? TeamId, string Name, LabourTotalsResponse Totals, decimal? LabourShare);

/// <summary>
/// Design labour over the last months: totals, month by month, by project and by department, with the part logged
/// outside the agreed scope. No figure is about one person.
/// </summary>
/// <param name="Months">Oldest first, ending with this month (to today).</param>
/// <param name="Projects">Projects with labour in these months, internal ones included, the costliest first.</param>
/// <param name="Departments">The costliest first.</param>
/// <param name="Unconverted">Labour with no exchange rate on its day, per currency: in no cost.</param>
public sealed record LabourInsightResponse(
    string Domain,
    LabourTotalsResponse Total,
    IReadOnlyList<LabourMonthResponse> Months,
    IReadOnlyList<LabourProjectResponse> Projects,
    IReadOnlyList<LabourDepartmentResponse> Departments,
    IReadOnlyList<CurrencyAmountResponse> Unconverted,
    InsightBasisResponse Basis);
