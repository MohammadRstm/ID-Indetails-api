using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.SiteExpenses;

/// <summary>
/// Expenses recorded on an Execution site (materials, a contractor, equipment, fees…), a cost and nothing charged to the client:
/// <list type="bullet">
/// <item>The cost is quantity × unit cost when both are known, otherwise the lump-sum amount.</item>
/// <item>The finalise lock and who may change a record are shared with ground labour (<see cref="Execution.SiteRules"/>).</item>
/// </list>
/// </summary>
public static class SiteExpenseRules
{
    /// <summary>What was typed: an item and a cost, either quantity × unit cost or a total.</summary>
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

    /// <summary>What expenses are grouped by (their item, their supplier): the text trimmed, with single spaces, in lower case.</summary>
    public static string GroupKey(string name)
        => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>
    /// A cost in another currency needs a rate in force on its day: the site's money leaves out what it can't convert,
    /// so without one the cost would never reach profit or the profit floor.
    /// </summary>
    public static RuleDenial? CheckConvertible(IEnumerable<(string Currency, DateOnly Day)> costs, ExchangeRateTable rates)
    {
        foreach (var (currency, day) in costs)
        {
            if (rates.ToUsd(1m, currency, day) is null)
            {
                return RuleDenial.BusinessRule(ErrorCodes.ExchangeRateMissing,
                    $"There is no {currency} exchange rate in force on {day:yyyy-MM-dd}. Add one first, or record the expense in USD.");
            }
        }

        return null;
    }
}
