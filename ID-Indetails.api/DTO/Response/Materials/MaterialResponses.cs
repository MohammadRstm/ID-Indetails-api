using ID_Indetails.api.Business.Materials;
using ID_Indetails.api.DTO.Response.Common;

namespace ID_Indetails.api.DTO.Response.Materials;

public sealed record MaterialResponse
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal? Quantity { get; init; }

    public string? Unit { get; init; }

    public decimal? UnitCost { get; init; }

    /// <summary>Quantity × unit cost when both are known, otherwise the total typed in.</summary>
    public decimal Cost { get; init; }

    public string Currency { get; init; } = "USD";

    public string? Supplier { get; init; }

    public string? Note { get; init; }

    public DateTime AddedAt { get; init; }

    /// <summary>The day it was added, in the company time zone.</summary>
    public DateOnly AddedOn { get; init; }

    public PersonRefResponse AddedBy { get; init; } = null!;

    public DateTime? UpdatedAt { get; init; }

    /// <summary>On a client statement: its type, cost and price are frozen and it can't be removed.</summary>
    public bool IsStated { get; init; }

    /// <summary>Whether the caller may change or remove it now.</summary>
    public bool CanEdit { get; init; }

    /// <summary>Only for callers who see money.</summary>
    public decimal? ClientPrice { get; init; }

    /// <summary>Only for callers who see money.</summary>
    public MaterialRules.PriceSource? PriceSource { get; init; }

    /// <summary>Client price ÷ cost − 1. Only for callers who see money.</summary>
    public decimal? Markup { get; init; }
}

/// <param name="Charge">What the client is charged for them; only for callers who see money.</param>
public sealed record MaterialTotalsResponse(int Count, decimal Cost, decimal? Charge);

/// <param name="Key">The type materials are grouped by: the name trimmed, in lower case.</param>
/// <param name="Label">The name as most recently typed.</param>
public sealed record MaterialTypeResponse(string Key, string Label, int Count, decimal Cost);

public sealed record MaterialSupplierResponse(string Key, string Label);

/// <param name="Matching">Totals of everything the filters match, across all pages.</param>
/// <param name="All">Totals of every material of the project.</param>
/// <param name="CanAdd">Whether the caller may add materials now.</param>
/// <param name="DefaultMarkup">The project's markup; only for callers who see money.</param>
public sealed record MaterialListResponse(
    IReadOnlyList<MaterialResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    MaterialTotalsResponse Matching,
    MaterialTotalsResponse All,
    IReadOnlyList<MaterialTypeResponse> Types,
    IReadOnlyList<MaterialSupplierResponse> Suppliers,
    IReadOnlyList<PersonRefResponse> AddedBy,
    bool CanAdd,
    decimal? DefaultMarkup);
