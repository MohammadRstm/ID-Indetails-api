using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Finance;

/// <summary>Costs dated in the period, in USD, by source.</summary>
public sealed record PeriodCostResponse(decimal Labour, decimal Expenses, decimal Materials, decimal GroundLabour, decimal Total);

/// <summary>Billing in USD. See Business/Billing/ProjectBilling.</summary>
/// <param name="Value">The revenue of the client projects covered.</param>
/// <param name="OldestOverdueDays">Days past due of the oldest overdue invoice.</param>
/// <param name="PaymentCount">Live payments, on account or not.</param>
public sealed record BillingPositionResponse(
    decimal Value,
    decimal Invoiced,
    int IssuedCount,
    decimal Received,
    decimal OnAccount,
    decimal Outstanding,
    decimal Overdue,
    int OverdueCount,
    int? OldestOverdueDays,
    decimal NotYetInvoiced,
    decimal Drafts,
    int PaymentCount);

/// <summary>A domain's money over a period (the dashboard's money cards and the Financials header).</summary>
/// <param name="Revenue">Recognised in the period (by progress, spread over each project's cost months).</param>
/// <param name="Unconverted">Costs of the period with no exchange rate on their day, in their own currency.</param>
/// <param name="UnpricedLabourMinutes">Work by people with no pay record, all time.</param>
/// <param name="StillToEarn">Client revenue not recognised yet.</param>
/// <param name="Billing">As of today, whatever the period.</param>
public sealed record FinanceSummaryResponse(
    string Domain,
    DateOnly From,
    DateOnly To,
    decimal Revenue,
    PeriodCostResponse Cost,
    decimal Profit,
    decimal? Margin,
    IReadOnlyList<UnconvertedCostResponse> Unconverted,
    int UnpricedLabourMinutes,
    int ClientProjects,
    int InternalProjects,
    decimal AllTimeClientRevenue,
    decimal AllTimeClientProfit,
    decimal AllTimeInternalCost,
    decimal StillToEarn,
    BillingPositionResponse Billing);

/// <param name="Unconverted">Costs of the month with no exchange rate on their day, in their own currency.</param>
public sealed record FinanceMonthResponse(
    DateOnly Month,
    decimal Revenue,
    decimal Labour,
    decimal Expenses,
    decimal Materials,
    decimal GroundLabour,
    decimal Cost,
    decimal Profit,
    IReadOnlyList<UnconvertedCostResponse> Unconverted);

/// <summary>A project's all-time money, as the Financials projects table shows it.</summary>
public sealed record FinanceProjectResponse(
    long ProjectId,
    string Title,
    string? ClientName,
    ProjectStatus Status,
    BillingType BillingType,
    decimal Revenue,
    decimal RecognisedToDate,
    decimal Hours,
    ProjectCostResponse Cost,
    decimal Profit,
    decimal? Margin,
    bool MarginKnown,
    ProjectProjectionResponse? Projected,
    decimal CostPerHour);

/// <param name="ClientProfit">Profit of the client projects; internal projects are cost only.</param>
public sealed record FinanceProjectTotalsResponse(
    int ClientProjects,
    int InternalProjects,
    decimal ClientRevenue,
    decimal ClientCost,
    decimal ClientProfit,
    decimal InternalCost,
    decimal Cost,
    IReadOnlyList<UnconvertedCostResponse> Unconverted);

public sealed record FinanceProjectsResponse(IReadOnlyList<FinanceProjectResponse> Projects, FinanceProjectTotalsResponse Totals);

/// <summary>Days past the due date: <c>ToDays</c> 0 is "not yet due", a null <c>ToDays</c> is open-ended.</summary>
public sealed record AgingResponse(int FromDays, int? ToDays, decimal Amount, int Count);

public sealed record BillingMonthResponse(DateOnly Month, decimal Invoiced, decimal Collected);

public sealed record ClientBillingResponse(long ClientId, string Name, decimal Value, decimal Invoiced, decimal Received, decimal Outstanding, decimal Overdue);

public sealed record MethodTotalResponse(PaymentMethod Method, decimal Amount);

public sealed record OpenInvoiceResponse(
    long Id,
    string? Number,
    long ProjectId,
    string ProjectTitle,
    string ClientName,
    DateOnly? DueOn,
    decimal Balance,
    bool IsOverdue,
    int DaysOverdue);

public sealed record UnconvertedInvoicesResponse(string Currency, decimal Amount, int Count);

/// <summary>The Financials billing and collections tab, in USD.</summary>
/// <param name="OpenInvoices">Sent or partly paid invoices, soonest due first.</param>
/// <param name="UnconvertedInvoices">Invoices with no exchange rate on their day, left out of every figure above.</param>
public sealed record FinanceBillingResponse(
    BillingPositionResponse Position,
    IReadOnlyList<AgingResponse> Aging,
    IReadOnlyList<BillingMonthResponse> Monthly,
    IReadOnlyList<ClientBillingResponse> ByClient,
    IReadOnlyList<MethodTotalResponse> ByMethod,
    IReadOnlyList<OpenInvoiceResponse> OpenInvoices,
    IReadOnlyList<UnconvertedInvoicesResponse> UnconvertedInvoices);

public sealed record PersonCostResponse(long UserId, string Name, decimal Usd, IReadOnlyList<CurrencyTotalResponse> Unconverted);

public sealed record CategoryCostResponse(string Category, decimal Usd);

/// <param name="Usd">Null when there was no exchange rate that day.</param>
public sealed record ExpenseItemResponse(long Id, long ProjectId, string ProjectTitle, string Label, string Category, DateOnly SpentOn, decimal Amount, string Currency, decimal? Usd);

/// <summary>The Financials costs tab for a period, in USD.</summary>
/// <param name="LabourByPerson">The 10 people who cost most.</param>
/// <param name="BiggestExpenses">The 12 biggest expenses, in USD.</param>
/// <param name="Unconverted">Costs of the period with no exchange rate on their day (every source), in their own currency.</param>
public sealed record FinanceCostsResponse(
    DateOnly From,
    DateOnly To,
    PeriodCostResponse BySource,
    IReadOnlyList<PersonCostResponse> LabourByPerson,
    IReadOnlyList<CategoryCostResponse> ExpensesByCategory,
    IReadOnlyList<ExpenseItemResponse> BiggestExpenses,
    IReadOnlyList<UnconvertedCostResponse> Unconverted);

/// <summary>Billing figures of one project or client, in USD.</summary>
public sealed record BillingTotalsResponse(decimal Invoiced, decimal Received, decimal Outstanding, decimal Overdue, decimal NotYetInvoiced, decimal Drafts);

/// <summary>The money shown on a row of the projects list.</summary>
/// <param name="HasUnconverted">A cost or an invoice is left out of these USD figures for want of an exchange rate.</param>
/// <param name="UnpricedLabourMinutes">Work by people with no pay record, left out of the cost.</param>
public sealed record ProjectListMoneyResponse(
    decimal Revenue,
    decimal RecognisedToDate,
    decimal Cost,
    decimal Profit,
    decimal? Margin,
    bool MarginKnown,
    decimal Invoiced,
    decimal Outstanding,
    bool HasUnconverted,
    int UnpricedLabourMinutes);

/// <summary>A client's money across their projects, all time, with the project margin rule applied to the client as a whole.</summary>
/// <param name="CompletedMargin">Margin of the completed and archived projects only.</param>
/// <param name="UnconvertedInvoices">Invoices left out of the billing figures for want of an exchange rate.</param>
public sealed record ClientMoneyResponse(
    int Projects,
    int ActiveProjects,
    int CompletedProjects,
    decimal Hours,
    decimal Revenue,
    decimal RecognisedToDate,
    ProjectCostResponse Cost,
    decimal Profit,
    decimal? Margin,
    bool MarginKnown,
    decimal ActiveRevenue,
    decimal CompletedRevenue,
    decimal? CompletedMargin,
    BillingTotalsResponse Billing,
    IReadOnlyList<UnconvertedInvoicesResponse> UnconvertedInvoices);

/// <param name="HasUnconverted">A cost or an invoice is left out of these USD figures for want of an exchange rate.</param>
/// <param name="UnpricedLabourMinutes">Work by people with no pay record, left out of the cost.</param>
public sealed record ClientProjectMoneyResponse(
    long ProjectId,
    string Title,
    ProjectStatus Status,
    decimal Hours,
    decimal Revenue,
    decimal Cost,
    decimal Profit,
    decimal? Margin,
    bool MarginKnown,
    BillingTotalsResponse Billing,
    DateOnly StartDate,
    bool HasUnconverted,
    int UnpricedLabourMinutes);

/// <summary>A money follow-up of the dashboard (see <c>Business/Finance/MoneyAttention</c>).</summary>
/// <param name="InvoiceNumber">Overdue invoice only, like <paramref name="InvoiceId"/> and <paramref name="DaysOverdue"/>.</param>
/// <param name="Amount">Overdue invoice: its balance. Finished but owed: what is outstanding. Cost warning: the cost.</param>
/// <param name="Revenue">Cost warning: the payment.</param>
/// <param name="UsedPct">Cost warning: the cost as a whole percentage of the payment.</param>
public sealed record MoneyAttentionResponse(
    MoneyAttentionKind Kind,
    long ProjectId,
    string ProjectTitle,
    string? ClientName,
    long? InvoiceId,
    string? InvoiceNumber,
    int? DaysOverdue,
    decimal? Amount,
    decimal? Revenue,
    int? UsedPct);

public sealed record FinanceClientResponse(long ClientId, string Name, string? Label, bool IsArchived, ClientMoneyResponse Money);

/// <summary>The money of every project the list's filters match, in USD, for the header above the list.</summary>
/// <param name="Profit">Client projects only.</param>
/// <param name="InternalCost">What internal projects cost.</param>
/// <param name="HasUnconverted">A cost or invoice of one of them has no exchange rate, so the figures leave it out.</param>
public sealed record ProjectListTotalsResponse(
    int Projects,
    int ClientProjects,
    int InternalProjects,
    decimal Revenue,
    decimal Cost,
    decimal Profit,
    decimal InternalCost,
    bool HasUnconverted,
    string Currency);
