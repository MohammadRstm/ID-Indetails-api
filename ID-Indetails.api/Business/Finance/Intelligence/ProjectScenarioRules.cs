using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance.Intelligence;

/// <summary>
/// One project's money with changes applied, nothing saved (owner decision Q18): a variation order, an extra cost, a
/// supervision or TVA rate, settling what the client still owes, a delay at the current weekly cost. Prices go through
/// the same rules as the real changes (<see cref="ExecutionPricing"/>, <see cref="Tva"/>), and profit is agreed revenue −
/// cost + pocket money, as everywhere (Q1).
/// </summary>
public static class ProjectScenarioRules
{
    /// <param name="Revenue">Agreed, net of TVA: a Design project's payment with its applied VOs; a site's work + supervision + permits.</param>
    /// <param name="Work">A site's work price (its project fee); 0 on Design.</param>
    /// <param name="Untaxed">Design: applied VOs that carry no TVA.</param>
    /// <param name="Received">What the client paid, TVA included.</param>
    public sealed record Facts(
        string Domain,
        BillingType BillingType,
        decimal Revenue,
        decimal Work,
        decimal Permits,
        decimal SupervisionRate,
        decimal TvaRate,
        decimal Untaxed,
        decimal Cost,
        decimal PocketNet,
        decimal Received);

    /// <param name="VoAmount">A VO on the work (a site) or the price (Design), net of TVA; negative takes work out.</param>
    /// <param name="ExtraCost">Added to the cost, in USD.</param>
    /// <param name="Settle">The price lowered to what the client paid, as completing with what is owed excused or written off does.</param>
    /// <param name="DelayCost">The weekly cost so far × the weeks of delay.</param>
    public sealed record Levers(
        decimal VoAmount = 0,
        decimal ExtraCost = 0,
        decimal? SupervisionRate = null,
        decimal? TvaRate = null,
        bool Settle = false,
        decimal DelayCost = 0);

    /// <param name="Standing">Against the profit floor; null where the floor doesn't apply (internal work, no agreed amount).</param>
    public sealed record Figures(
        decimal Revenue,
        decimal RevenueWithTva,
        decimal Cost,
        decimal Profit,
        decimal? Margin,
        decimal Owed,
        ProfitFloorRules.Standing? Standing);

    /// <param name="FloorWouldRefuse">
    /// The changes other than a settlement take profit to zero or below and lower it: as real changes they would be refused
    /// (<c>profit_floor</c>) and kept as a blocked change. A settlement isn't held to the floor (Q10).
    /// </param>
    /// <param name="SettlementImpossible">A site whose client paid less than its permits alone: no work price makes what they paid the price.</param>
    public sealed record Outcome(Figures Before, Figures After, bool FloorWouldRefuse, bool SettlementImpossible);

    public static Outcome Run(Facts facts, Levers levers)
    {
        var before = FiguresOf(facts, facts.Revenue, Gross(facts, facts.Work, facts.Revenue, facts.SupervisionRate, facts.TvaRate), facts.Work, facts.Cost);
        var unsettled = Apply(facts, levers with { Settle = false }, out _);
        var after = levers.Settle ? Apply(facts, levers, out var impossible) : unsettled;
        var refused = before.Standing is not null && unsettled.Profit <= 0 && unsettled.Profit < before.Profit;
        return new Outcome(before, after, refused, levers.Settle && impossible);
    }

    private static Figures Apply(Facts facts, Levers levers, out bool settlementImpossible)
    {
        settlementImpossible = false;
        var cost = facts.Cost + levers.ExtraCost + levers.DelayCost;
        var tvaRate = levers.TvaRate ?? facts.TvaRate;
        if (facts.BillingType != BillingType.Client)
        {
            return FiguresOf(facts, 0, 0, 0, cost);
        }

        if (facts.Domain == DomainCodes.Execution)
        {
            var rate = levers.SupervisionRate ?? facts.SupervisionRate;
            var work = facts.Work + levers.VoAmount;
            if (levers.Settle)
            {
                var settled = ExecutionPricing.WorkFor(facts.Received, facts.Permits, rate, tvaRate);
                settlementImpossible = settled < 0;
                work = settlementImpossible ? work : settled;
            }

            var price = ExecutionPricing.PriceOf(work, facts.Permits, rate, tvaRate);
            return FiguresOf(facts, price.Net, price.Gross, work, cost);
        }

        var revenue = levers.Settle
            ? Tva.NetFor(facts.Received, tvaRate, facts.Untaxed)
            : facts.Revenue + levers.VoAmount;
        return FiguresOf(facts, revenue, Tva.Gross(revenue, tvaRate, facts.Untaxed), 0, cost);
    }

    private static decimal Gross(Facts facts, decimal work, decimal revenue, decimal supervisionRate, decimal tvaRate)
        => facts.BillingType != BillingType.Client ? 0
            : facts.Domain == DomainCodes.Execution ? ExecutionPricing.PriceOf(work, facts.Permits, supervisionRate, tvaRate).Gross
            : Tva.Gross(revenue, tvaRate, facts.Untaxed);

    private static Figures FiguresOf(Facts facts, decimal revenue, decimal gross, decimal work, decimal cost)
    {
        var profit = revenue - cost + facts.PocketNet;
        var applies = ProfitFloorRules.Applies(facts.BillingType, facts.Domain, revenue, work);
        return new Figures(
            revenue,
            gross,
            cost,
            profit,
            ProjectFinance.MarginOf(profit, revenue),
            facts.BillingType == BillingType.Client ? Math.Max(0, gross - facts.Received) : 0,
            applies ? ProfitFloorRules.StandingOf(profit, revenue) : null);
    }
}
