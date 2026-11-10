using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Insights;

/// <param name="Amount">The USD figure the risk is about (net of TVA), where there is one: a loss, a projected profit, an overspend, the profit a settlement took.</param>
/// <param name="Fact">What was measured, in words.</param>
/// <param name="Rule">The rule that fired, in words.</param>
public sealed record ProjectRiskResponse(ProjectRiskKind Kind, decimal? Amount, string Fact, string Rule);

/// <summary>One project and everything that fired for it, most pressing first.</summary>
public sealed record ProjectRiskGroupResponse(long ProjectId, string Title, ProjectStatus Status, IReadOnlyList<ProjectRiskResponse> Risks);

/// <summary>How many projects have a risk of this kind.</summary>
public sealed record RiskCountResponse(ProjectRiskKind Kind, int Projects);

/// <summary>
/// A domain's client projects with a financial risk, ranked by their most pressing one: each risk names the rule that
/// fired. Internal projects are cost centres and never listed.
/// </summary>
/// <param name="Checked">How many projects were checked.</param>
public sealed record ProjectRisksResponse(
    string Domain,
    DateOnly Today,
    int Checked,
    IReadOnlyList<ProjectRiskGroupResponse> Projects,
    IReadOnlyList<RiskCountResponse> Counts,
    InsightBasisResponse Basis);

/// <param name="Usd">The category's spending that month, in USD at each expense's day.</param>
/// <param name="Baseline">Its average over the months before (empty months count as 0); null while there isn't that much history or they were all empty.</param>
public sealed record ExpenseCategoryMonthResponse(string Category, decimal Usd, decimal? Baseline);

/// <summary>One month's spending, every category in the order of <see cref="ExpenseTrendsResponse.Categories"/>.</summary>
public sealed record ExpenseMonthResponse(DateOnly Month, decimal Total, IReadOnlyList<ExpenseCategoryMonthResponse> Categories);

/// <summary>Spending that a rule called unusual.</summary>
/// <param name="On">The first day of the month for a category's month; the expense's day otherwise.</param>
/// <param name="ProjectId">Null for a category's month, which covers the whole domain.</param>
/// <param name="Usd">The month's total, the expense's cost, or one matching expense's cost, in USD; null with no exchange rate.</param>
/// <param name="Baseline">What it was judged by: the category's average month, or the item's average unit cost (USD).</param>
/// <param name="Pct">How far above that, as a whole percentage.</param>
/// <param name="ExpenseIds">The expenses it is about; empty for a category's month.</param>
public sealed record ExpenseAnomalyResponse(
    ExpenseAnomalyKind Kind,
    DateOnly On,
    string? Category,
    long? ProjectId,
    string? ProjectTitle,
    string? Item,
    decimal? Usd,
    decimal? Baseline,
    int? Pct,
    IReadOnlyList<long> ExpenseIds,
    string Fact,
    string Rule);

/// <summary>
/// A domain's spending by category, month by month, in USD at each expense's day, and what the owner's rules call
/// unusual in it. Design: project expenses by their category. Execution: site expenses by theirs.
/// </summary>
/// <param name="Categories">Every category of the domain's scheme, in its order.</param>
/// <param name="Months">Oldest first, ending with this month (to today).</param>
/// <param name="Anomalies">Newest first.</param>
/// <param name="Unconverted">Spending in these months with no exchange rate on its day, per currency: in no USD figure.</param>
public sealed record ExpenseTrendsResponse(
    string Domain,
    IReadOnlyList<string> Categories,
    IReadOnlyList<ExpenseMonthResponse> Months,
    IReadOnlyList<ExpenseAnomalyResponse> Anomalies,
    IReadOnlyList<CurrencyAmountResponse> Unconverted,
    InsightBasisResponse Basis);
