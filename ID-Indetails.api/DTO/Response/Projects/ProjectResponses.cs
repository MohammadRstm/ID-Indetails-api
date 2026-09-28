using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Projects;

public sealed record ProjectClientResponse(long Id, string DisplayName);

public sealed record ProjectOwnerResponse(long Id, string Name);

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

    public decimal? ProjectFee { get; init; }

    public decimal? MaterialMarkup { get; init; }

    /// <summary>Execution: what the client is charged for materials.</summary>
    public decimal? MaterialCharge { get; init; }

    /// <summary>Project expenses, one total per currency.</summary>
    public IReadOnlyList<CurrencyTotalResponse> ExpenseTotals { get; init; } = [];

    public ProjectBillingResponse Billing { get; init; } = null!;
}

/// <summary>The project's cash position in USD, kept apart from revenue and profit.</summary>
/// <param name="Invoiced">Issued, live invoices.</param>
/// <param name="Outstanding">Left to pay on those invoices.</param>
/// <param name="Received">Every live payment, on account or not.</param>
/// <param name="OnAccount">Live payments not tied to an invoice.</param>
/// <param name="NotYetInvoiced">Revenue minus invoiced, never below zero.</param>
/// <param name="Owed">Revenue minus received, never below zero.</param>
/// <param name="PaidAhead">Received beyond the revenue.</param>
public sealed record ProjectBillingResponse(
    decimal Invoiced,
    decimal Outstanding,
    decimal Received,
    decimal OnAccount,
    decimal NotYetInvoiced,
    decimal Owed,
    decimal PaidAhead);

public sealed record ProjectSummaryResponse
{
    public long Id { get; init; }

    public string Domain { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? Icon { get; init; }

    public string? Tint { get; init; }

    public ProjectStatus Status { get; init; }

    public BillingType BillingType { get; init; }

    public ProjectClientResponse? Client { get; init; }

    public ProjectOwnerResponse Owner { get; init; } = null!;

    public DateOnly StartDate { get; init; }

    public DateOnly? TargetDate { get; init; }

    public int ProgressPct { get; init; }
}

public sealed record ProjectResponse
{
    public long Id { get; init; }

    public string Domain { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

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

    public int ProgressPct { get; init; }

    public DateTime CreatedAt { get; init; }

    public ProjectMoneyResponse? Money { get; init; }
}
