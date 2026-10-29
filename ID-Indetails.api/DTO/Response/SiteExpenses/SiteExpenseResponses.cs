using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.SiteExpenses;

public sealed record SiteExpenseResponse
{
    public long Id { get; init; }

    /// <summary>The item.</summary>
    public string Name { get; init; } = string.Empty;

    public decimal? Quantity { get; init; }

    public string? Unit { get; init; }

    public decimal? UnitCost { get; init; }

    /// <summary>Quantity × unit cost when both are known, otherwise the total typed in.</summary>
    public decimal Cost { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>Who was paid.</summary>
    public string? Supplier { get; init; }

    public string? Note { get; init; }

    /// <summary>The day the money was spent: what the cost is dated by.</summary>
    public DateOnly SpentOn { get; init; }

    public SiteExpenseCategory Category { get; init; }

    public AccountLabelRefResponse? AccountLabel { get; init; }

    public DateTime AddedAt { get; init; }

    /// <summary>The day it was added, in the company time zone.</summary>
    public DateOnly AddedOn { get; init; }

    public PersonRefResponse AddedBy { get; init; } = null!;

    public DateTime? UpdatedAt { get; init; }

    /// <summary>Whether the caller may change or remove it now.</summary>
    public bool CanEdit { get; init; }
}

/// <param name="Cost">The total of the rows in USD; the other currencies are in <paramref name="ByCurrency"/>.</param>
/// <param name="ByCurrency">One total per currency, USD included. Amounts in different currencies are never added together.</param>
public sealed record SiteExpenseTotalsResponse(int Count, decimal Cost, IReadOnlyList<CurrencyTotalResponse> ByCurrency);

/// <param name="Key">The type expenses are grouped by: the item name trimmed, in lower case.</param>
/// <param name="Label">The name as most recently typed.</param>
/// <param name="Cost">The total of the rows in USD.</param>
public sealed record SiteExpenseTypeResponse(string Key, string Label, int Count, decimal Cost);

public sealed record SiteExpenseSupplierResponse(string Key, string Label);

/// <param name="Matching">Totals of everything the filters match, across all pages.</param>
/// <param name="All">Totals of every expense of the project.</param>
/// <param name="CanAdd">Whether the caller may add expenses now.</param>
public sealed record SiteExpenseListResponse(
    IReadOnlyList<SiteExpenseResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    SiteExpenseTotalsResponse Matching,
    SiteExpenseTotalsResponse All,
    IReadOnlyList<SiteExpenseTypeResponse> Types,
    IReadOnlyList<SiteExpenseSupplierResponse> Suppliers,
    IReadOnlyList<PersonRefResponse> AddedBy,
    bool CanAdd);
