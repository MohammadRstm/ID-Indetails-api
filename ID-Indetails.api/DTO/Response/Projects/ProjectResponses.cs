using System.Text.Json.Serialization;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Projects;

public sealed record ProjectClientResponse(long Id, string DisplayName);

public sealed record ProjectOwnerResponse(long Id, string Name);

/// <summary>One change of a project's agreed payment, newest first in a list.</summary>
/// <param name="Amount">USD: Design's customer payment, Execution's project fee.</param>
/// <param name="PreviousAmount">Null for the payment agreed at the start.</param>
/// <param name="MaterialMarkup">Execution only: the default material markup after it, a fraction (0.125 is 12.5%).</param>
/// <param name="ChangedBy">Null when the person no longer exists.</param>
public sealed record ProjectPaymentChangeResponse(
    long Id,
    decimal Amount,
    decimal? PreviousAmount,
    decimal? MaterialMarkup,
    decimal? PreviousMaterialMarkup,
    string Description,
    DateTime ChangedAt,
    ProjectOwnerResponse? ChangedBy,
    decimal? TvaRate = null,
    decimal? PreviousTvaRate = null);

/// <summary>Only for callers with <c>financial:view</c>.</summary>
public sealed record ProjectMoneyResponse
{
    public string Currency { get; init; } = "USD";

    /// <summary>
    /// Design: the agreed revenue. Execution: the agreed amount (material client prices plus the fee),
    /// calculated live while the project is open and frozen when it is finalised.
    /// </summary>
    public decimal Revenue { get; init; }

    /// <summary>Revenue, fee and markup can't change until the project is reopened.</summary>
    public bool IsLocked { get; init; }

    /// <summary>TVA on what the client pays, 0.15 = 15%. Revenue and profit are net of it.</summary>
    public decimal TvaRate { get; init; }

    /// <summary>The agreed payment (Design) or agreed amount before materials (Execution, the fee), TVA included.</summary>
    public decimal PaymentWithTva { get; init; }

    /// <summary>Revenue with TVA included: what the client is agreed to pay so far.</summary>
    public decimal RevenueWithTva { get; init; }

    public decimal? ProjectFee { get; init; }

    public decimal? MaterialMarkup { get; init; }

    /// <summary>Execution: the client prices of the materials sent to the client on a statement, which are part of revenue.</summary>
    public decimal? MaterialCharge { get; init; }

    /// <summary>Execution: the client prices of the materials not sent yet; sending them on a statement adds them to revenue.</summary>
    public decimal? UnsentMaterialCharge { get; init; }

    /// <summary>Execution: how many materials haven't been sent to the client yet.</summary>
    public int UnsentMaterials { get; init; }

    /// <summary>Project expenses, one total per currency.</summary>
    public IReadOnlyList<CurrencyTotalResponse> ExpenseTotals { get; init; } = [];

    public ProjectBillingResponse Billing { get; init; } = null!;

    public ProjectCostResponse Cost { get; init; } = null!;

    public decimal Profit { get; init; }

    /// <summary>Profit ÷ revenue; null until the project is done, 5% along or has 10 hours logged.</summary>
    public decimal? Margin { get; init; }

    public decimal RecognisedToDate { get; init; }

    /// <summary>Where cost, profit and margin are heading at the current progress. Open client projects 5%+ along.</summary>
    public ProjectProjectionResponse? Projected { get; init; }

    public decimal LabourHours { get; init; }

    public decimal CostPerHour { get; init; }

    /// <summary>Execution: received − cost − moved to the pocket money + paid by it.</summary>
    public decimal? CashProfit { get; init; }

    /// <summary>Execution: what was moved from the project into the pocket money. Lowers its profit, not what the client owes.</summary>
    public decimal? ToPocket { get; init; }

    /// <summary>Execution: what the pocket money paid to the project. Raises its profit.</summary>
    public decimal? FromPocket { get; init; }

    /// <summary>Execution: what the materials sent earn, their charge − their cost.</summary>
    public decimal? MaterialProfit { get; init; }

    /// <summary>Execution: materials left out of the charge because no exchange rate covered their day.</summary>
    public int UnconvertedMaterials { get; init; }
}

/// <summary>
/// Cost in USD, each amount converted at the exchange rate of its day. Amounts with no rate that day
/// are listed in <see cref="Unconverted"/> (in their own currency) instead of being added in.
/// </summary>
/// <param name="UnpricedLabourMinutes">Worked time of people with no pay record.</param>
public sealed record ProjectCostResponse(
    decimal Labour,
    decimal Expenses,
    decimal Materials,
    decimal GroundLabour,
    decimal Total,
    IReadOnlyList<UnconvertedCostResponse> Unconverted,
    int UnpricedLabourMinutes);

public sealed record UnconvertedCostResponse(CostSource Source, string Currency, decimal Amount);

public sealed record ProjectProjectionResponse(decimal Cost, decimal Profit, decimal? Margin);

/// <summary>The project's cash position in USD, kept apart from revenue and profit.</summary>
/// <param name="Invoiced">Issued, live invoices.</param>
/// <param name="Outstanding">Left to pay on those invoices.</param>
/// <param name="Received">Every live payment, on account or not.</param>
/// <param name="OnAccount">Live payments not tied to an invoice.</param>
/// <param name="NotYetInvoiced">Revenue neither invoiced nor received on account, never below zero.</param>
/// <param name="LeftToInvoice">Revenue minus invoiced, never below zero: what a new invoice can still be for.</param>
/// <param name="Owed">Revenue minus received, never below zero.</param>
/// <param name="PaidAhead">Received beyond the revenue.</param>
/// <param name="IssuedCount">Issued, live invoices.</param>
/// <param name="PaymentCount">Live payments, on account or not.</param>
/// <param name="Overdue">Left to pay on invoices past their due date.</param>
/// <param name="OldestOverdueDays">Days past due of the oldest overdue invoice.</param>
/// <param name="Drafts">Invoices drafted but not issued yet.</param>
/// <param name="UnconvertedInvoices">Invoices left out of these figures for want of an exchange rate on their day.</param>
public sealed record ProjectBillingResponse(
    decimal Invoiced,
    decimal Outstanding,
    decimal Received,
    decimal OnAccount,
    decimal NotYetInvoiced,
    decimal LeftToInvoice,
    decimal Owed,
    decimal PaidAhead,
    int IssuedCount,
    int PaymentCount,
    decimal Overdue,
    int OverdueCount,
    int? OldestOverdueDays,
    decimal Drafts,
    IReadOnlyList<UnconvertedInvoicesResponse> UnconvertedInvoices);

public sealed record ProjectSummaryResponse
{
    public long Id { get; init; }

    public string Domain { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>Shown under the project's name in lists and on employees' project cards.</summary>
    public string? Description { get; init; }

    /// <summary>Where the work is, when given.</summary>
    public string? Location { get; init; }

    public string? Icon { get; init; }

    public string? Tint { get; init; }

    public ProjectStatus Status { get; init; }

    public BillingType BillingType { get; init; }

    public ProjectClientResponse? Client { get; init; }

    public ProjectOwnerResponse Owner { get; init; } = null!;

    public DateOnly StartDate { get; init; }

    public DateOnly? TargetDate { get; init; }

    public int ProgressPct { get; init; }

    /// <summary>Only for callers with financial:view; left out entirely otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProjectListMoneyResponse? Money { get; init; }

    /// <summary>Design projects only, for callers with work_entry:view_all; left out entirely otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProjectListWorkResponse? Work { get; init; }

    /// <summary>Execution sites only; left out entirely for Design projects.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SiteTeamResponse? Team { get; init; }
}

/// <summary>
/// An Execution site's team: the employees who oversee it.
/// </summary>
/// <param name="People">The first 5, in the order they were put on the site.</param>
/// <param name="Count">Everyone overseeing it.</param>
public sealed record SiteTeamResponse(IReadOnlyList<PersonRefResponse> People, int Count);

/// <summary>A Design project's logged work, as the project list shows it.</summary>
/// <param name="ActivityLevel">From the last 14 days: busy from 45 h a week, steady from 15 h, quiet with any work.</param>
/// <param name="Weekly">Minutes of the last 8 weeks, ending with this one, oldest first.</param>
/// <param name="Contributors">The 4 people who logged the most time on it.</param>
/// <param name="ContributorCount">Everyone who logged work on it.</param>
public sealed record ProjectListWorkResponse(
    int TotalMinutes,
    ActivityLevel ActivityLevel,
    int MinutesLast14Days,
    IReadOnlyList<int> Weekly,
    DateTime? LastStartedAt,
    IReadOnlyList<PersonRefResponse> Contributors,
    int ContributorCount);

public sealed record ProjectResponse
{
    public long Id { get; init; }

    public string Domain { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Where the work is, when given.</summary>
    public string? Location { get; init; }

    public string? Icon { get; init; }

    public string? Tint { get; init; }

    public ProjectStatus Status { get; init; }

    public BillingType BillingType { get; init; }

    public ProjectClientResponse? Client { get; init; }

    public ProjectOwnerResponse Owner { get; init; } = null!;

    public DateOnly StartDate { get; init; }

    public DateOnly? TargetDate { get; init; }

    public DateTime? CompletedAt { get; init; }

    public DateTime? FinalizedAt { get; init; }

    /// <summary>When it was put on hold, while it is on hold.</summary>
    public DateTime? HeldSince { get; init; }

    /// <summary>Execution sites only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SiteTeamResponse? Team { get; init; }

    /// <summary>Execution sites only: everyone overseeing it, in the order they were put on it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PersonRefResponse>? Staff { get; init; }

    public int ProgressPct { get; init; }

    public DateTime CreatedAt { get; init; }

    public ProjectMoneyResponse? Money { get; init; }
}
