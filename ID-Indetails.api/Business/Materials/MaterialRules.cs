using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Materials;

/// <summary>
/// Materials recorded on an Execution site:
/// <list type="bullet">
/// <item>The cost is quantity × unit cost when both are known, otherwise the lump-sum amount.</item>
/// <item>The finalise lock and who may change a record are shared with ground labour (<see cref="Execution.SiteRules"/>).</item>
/// <item>A stated material (on a client statement) keeps its type, cost and price, and can't be removed.</item>
/// </list>
/// </summary>
public static class MaterialRules
{
    public enum PriceSource
    {
        /// <summary>Cost × (1 + the project's markup).</summary>
        Auto,

        /// <summary>Set by hand.</summary>
        Manual,

        /// <summary>Frozen at the price shown on a client statement.</summary>
        Stated,
    }

    /// <summary>What was typed: a type and a cost, either quantity × unit cost or a total.</summary>
    public sealed record Entry(decimal? Quantity, decimal? UnitCost, decimal? Amount);

    public static decimal Cost(decimal? quantity, decimal? unitCost, decimal? amount)
        => quantity is > 0 && unitCost is > 0
            ? Math.Round(quantity.Value * unitCost.Value, 2, MidpointRounding.AwayFromZero)
            : amount ?? 0;

    /// <summary>
    /// The values to store. With quantity and unit cost both given, they decide the cost; a total
    /// typed over them that doesn't match sets the unit cost to total ÷ quantity instead. The
    /// lump-sum amount is kept only when quantity × unit cost can't give the cost.
    /// </summary>
    public static Entry Normalise(Entry entry)
    {
        if (entry.Quantity is not > 0 || entry.UnitCost is not > 0)
        {
            return entry;
        }

        var unitCost = entry.Amount is > 0 && entry.Amount != Cost(entry.Quantity, entry.UnitCost, null)
            ? Math.Round(entry.Amount.Value / entry.Quantity.Value, 4, MidpointRounding.AwayFromZero)
            : entry.UnitCost;

        return new Entry(entry.Quantity, unitCost, null);
    }

    /// <summary>A price typed equal to the automatic one is stored as automatic, so it keeps following the markup.</summary>
    public static decimal? ManualPrice(decimal? typed, decimal cost, decimal markup)
        => typed is { } price && price == ExecutionPricing.ClientPrice(new ExecutionPricing.Material(cost, null), markup)
            ? null
            : typed;

    public static PriceSource SourceOf(decimal? clientPrice, bool stated)
        => stated ? PriceSource.Stated : clientPrice is null ? PriceSource.Auto : PriceSource.Manual;

    public static RuleDenial? CheckNotStated(bool stated)
        => stated
            ? RuleDenial.BusinessRule(ErrorCodes.MaterialStated, "This material is on a client statement, so its type, cost and price can't change and it can't be removed.")
            : null;

    /// <summary>What materials are grouped by (their type, their supplier): the text trimmed, with single spaces, in lower case.</summary>
    public static string GroupKey(string name)
        => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
