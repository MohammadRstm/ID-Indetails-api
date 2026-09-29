using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance;

/// <summary>
/// A domain's money over a period, from each project's dated costs and recognised revenue (see <see cref="Recognition"/>):
/// <list type="bullet">
/// <item>Revenue: each month's share of the recognised revenue, weighted by how much of the month the period covers.</item>
/// <item>Cost: every cost dated inside the period, by source.</item>
/// <item>Margin: profit ÷ revenue while there is revenue.</item>
/// </list>
/// </summary>
public static class PeriodMoney
{
    public sealed record CostTotals(decimal Labour, decimal Expenses, decimal Materials, decimal GroundLabour)
    {
        public decimal Total => Labour + Expenses + Materials + GroundLabour;
    }

    public static decimal Revenue(IReadOnlyDictionary<DateOnly, decimal> byMonth, IReadOnlyList<Recognition.MonthShare> shares)
        => shares.Sum(s => byMonth.GetValueOrDefault(s.Month) * s.Accrued);

    public static CostTotals Costs(IEnumerable<Recognition.DatedCost> costs, DateOnly from, DateOnly to)
    {
        var inside = costs.Where(c => c.Day >= from && c.Day <= to).ToList();
        decimal Of(CostSource source) => inside.Where(c => c.Source == source).Sum(c => c.Usd);
        return new CostTotals(Of(CostSource.Labour), Of(CostSource.Expenses), Of(CostSource.Materials), Of(CostSource.GroundLabour));
    }

    public static decimal? Margin(decimal profit, decimal revenue)
        => revenue > 0 ? Math.Round(profit / revenue, 4, MidpointRounding.AwayFromZero) : null;

    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
