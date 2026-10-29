using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.SiteExpenses;

/// <summary>Filters for a site's expenses. Everything is optional.</summary>
public sealed record SiteExpenseListQuery
{
    /// <summary>A type key from the list's <c>types</c> (the item name trimmed, in lower case).</summary>
    public string? Type { get; init; }

    /// <summary>A supplier key from the list's <c>suppliers</c>.</summary>
    public string? Supplier { get; init; }

    public long? AddedById { get; init; }

    public SiteExpenseCategory? Category { get; init; }

    public long? AccountLabelId { get; init; }

    /// <summary>Spent on or after this day.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Spent on or before this day.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Matches item, supplier, note, unit or who added it.</summary>
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 30;
}

/// <summary>An expense as typed: an item and a cost, either quantity × unit cost or a total.</summary>
public record SiteExpenseEntryRequest
{
    /// <summary>The item ("Item" on screen).</summary>
    public string Name { get; init; } = string.Empty;

    public decimal? Quantity { get; init; }

    public string? Unit { get; init; }

    public decimal? UnitCost { get; init; }

    /// <summary>The total. With quantity and unit cost it is worked out; a different total sets the unit cost to total ÷ quantity.</summary>
    public decimal? Amount { get; init; }

    /// <summary>Who was paid ("Supplier / payee" on screen).</summary>
    public string? Supplier { get; init; }

    public string? Note { get; init; }

    /// <summary>USD or LBP. Left out: USD when adding, as it is when changing.</summary>
    public string? Currency { get; init; }

    /// <summary>The day the money was spent. Left out: today (company time zone) when adding, as it is when changing.</summary>
    public DateOnly? SpentOn { get; init; }

    /// <summary>Left out: <c>materials</c> when adding, as it is when changing.</summary>
    public SiteExpenseCategory? Category { get; init; }

    /// <summary>An account label to file it under. It must not be archived unless the expense already has it; left out when changing, it clears the label.</summary>
    public long? AccountLabelId { get; init; }
}

public sealed record NewSiteExpenseRequest : SiteExpenseEntryRequest
{
    /// <summary>
    /// The phone app's own id for this expense (a UUID). If the caller already added an expense with it, that one is
    /// returned instead of adding another.
    /// </summary>
    public Guid? ClientRef { get; init; }
}

public sealed record AddSiteExpensesRequest
{
    public IReadOnlyList<NewSiteExpenseRequest> Items { get; init; } = [];
}
