using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance;

/// <summary>
/// A domain's money over a period, live: it moves as soon as a payment is recorded or a cost is added.
/// <list type="bullet">
/// <item>Revenue: the money received in the period, each payment counted on the day it arrived.</item>
/// <item>Cost: every cost dated inside the period, by source. Costs with no exchange rate on their day are listed
/// apart, in their own currency (<see cref="Unconverted"/>).</item>
/// <item>Margin: profit ÷ revenue while there is revenue.</item>
/// </list>
/// </summary>
public static class PeriodMoney
{
    public sealed record CostTotals(decimal Labour, decimal Expenses, decimal SiteExpenses, decimal GroundLabour)
    {
        public decimal Total => Labour + Expenses + SiteExpenses + GroundLabour;
    }

    /// <summary>Revenue in a period: the payments received in it (in USD), each on the day it arrived.</summary>
    public static decimal Received(IEnumerable<(DateOnly Day, decimal Usd)> payments, DateOnly from, DateOnly to)
        => Round(payments.Where(p => p.Day >= from && p.Day <= to).Sum(p => p.Usd));

    public static CostTotals Costs(IEnumerable<Recognition.DatedCost> costs, DateOnly from, DateOnly to)
    {
        var inside = costs.Where(c => c.Day >= from && c.Day <= to).ToList();
        decimal Of(CostSource source) => inside.Where(c => c.Source == source).Sum(c => c.Usd);
        return new CostTotals(Of(CostSource.Labour), Of(CostSource.Expenses), Of(CostSource.SiteExpense), Of(CostSource.GroundLabour));
    }

    public static IReadOnlyList<ProjectFinance.UnconvertedCost> Unconverted(
        IEnumerable<ProjectFinance.DatedUnconverted> costs,
        DateOnly from,
        DateOnly to)
        => costs
            .Where(c => c.Day >= from && c.Day <= to)
            .GroupBy(c => (c.Source, c.Currency))
            .Select(g => new ProjectFinance.UnconvertedCost(g.Key.Source, g.Key.Currency, Round(g.Sum(c => c.Amount))))
            .OrderBy(u => u.Source)
            .ThenBy(u => u.Currency)
            .ToList();

    public static decimal? Margin(decimal profit, decimal revenue)
        => revenue > 0 ? Math.Round(profit / revenue, 4, MidpointRounding.AwayFromZero) : null;

    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
