namespace ID_Indetails.api.DTO.Request.Materials;

/// <summary>Filters for a project's materials. Everything is optional.</summary>
public sealed record MaterialListQuery
{
    /// <summary>A type key from the list's <c>types</c> (the name trimmed, in lower case).</summary>
    public string? Type { get; init; }

    /// <summary>A supplier key from the list's <c>suppliers</c>.</summary>
    public string? Supplier { get; init; }

    public long? AddedById { get; init; }

    /// <summary>Added on or after this day (company time zone).</summary>
    public DateOnly? From { get; init; }

    /// <summary>Added on or before this day (company time zone).</summary>
    public DateOnly? To { get; init; }

    /// <summary>Matches type, supplier, note, unit or who added it.</summary>
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 30;
}

/// <summary>A material as typed: a type and a cost, either quantity × unit cost or a total.</summary>
public record MaterialEntryRequest
{
    public string Name { get; init; } = string.Empty;

    public decimal? Quantity { get; init; }

    public string? Unit { get; init; }

    public decimal? UnitCost { get; init; }

    /// <summary>The total. With quantity and unit cost it is worked out; a different total sets the unit cost to total ÷ quantity.</summary>
    public decimal? Amount { get; init; }

    public string? Supplier { get; init; }

    public string? Note { get; init; }
}

public sealed record NewMaterialRequest : MaterialEntryRequest
{
    /// <summary>A price of its own for the client. Needs <c>project:pricing_manage</c>; empty means the project's markup.</summary>
    public decimal? ClientPrice { get; init; }
}

public sealed record AddMaterialsRequest
{
    public IReadOnlyList<NewMaterialRequest> Items { get; init; } = [];
}

public sealed record SetMaterialPriceRequest
{
    /// <summary>Null returns the material to the project's markup.</summary>
    public decimal? ClientPrice { get; init; }
}
