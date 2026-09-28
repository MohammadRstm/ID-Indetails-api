using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance;

/// <summary>
/// A project's cost, profit and margin, in USD. Every cost is converted at the company exchange rate
/// in force on the day it happened; a cost with no rate that day is left out and reported as unconverted.
/// <list type="bullet">
/// <item>Cost = employee labour (hours × the hourly cost in force that day) + expenses + materials + ground labour.</item>
/// <item>Profit = revenue − cost. Internal projects have no revenue.</item>
/// <item>Margin = profit ÷ revenue, only once it means something: the project is done, at least 5% along,
/// or has at least 10 hours logged.</item>
/// <item>Projected cost = cost ÷ progress, while an open client project is at least 5% along.</item>
/// <item>Revenue recognised to date = revenue × progress, or all of it once done.</item>
/// </list>
/// </summary>
public static class ProjectFinance
{
    public const int MinProgressForMargin = 5;
    public const int MinHoursForMargin = 10;

    public sealed record CostItem(CostSource Source, string Currency, decimal Amount, DateOnly Day);

    public sealed record UnconvertedCost(CostSource Source, string Currency, decimal Amount);

    public sealed record Costs(
        decimal Labour,
        decimal Expenses,
        decimal Materials,
        decimal GroundLabour,
        IReadOnlyList<UnconvertedCost> Unconverted)
    {
        public decimal Total => Labour + Expenses + Materials + GroundLabour;
    }

    public sealed record WorkedTime(long UserId, DateOnly Day, int Minutes);

    public sealed record PayRate(long UserId, DateOnly EffectiveFrom, decimal HourlyCost, string Currency);

    public sealed record Facts(bool IsClientWork, bool IsDone, int ProgressPct, decimal Revenue, int LabourMinutes);

    public sealed record Projection(decimal Cost, decimal Profit, decimal? Margin);

    public sealed record Summary(
        decimal Revenue,
        decimal RecognisedToDate,
        decimal Cost,
        decimal Profit,
        decimal? Margin,
        bool MarginKnown,
        Projection? Projected,
        decimal CostPerHour);

    /// <summary>
    /// Prices worked time at each person's hourly cost in force that day (their earliest rate for time
    /// before it). Time of someone with no pay record can't be priced and is counted separately.
    /// </summary>
    public static (IReadOnlyList<CostItem> Items, int UnpricedMinutes) PriceLabour(
        IEnumerable<WorkedTime> worked,
        IEnumerable<PayRate> rates)
    {
        var byUser = rates.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.EffectiveFrom).ToList());
        var items = new List<CostItem>();
        var unpriced = 0;

        foreach (var w in worked)
        {
            if (!byUser.TryGetValue(w.UserId, out var history))
            {
                unpriced += w.Minutes;
                continue;
            }

            var rate = RateOn(history, w.Day);
            items.Add(new CostItem(CostSource.Labour, rate.Currency, w.Minutes / 60m * rate.HourlyCost, w.Day));
        }

        return (items, unpriced);
    }

    /// <summary>The rate in force on the day, from a person's history ordered by date; their earliest rate for days before it.</summary>
    public static PayRate RateOn(IReadOnlyList<PayRate> history, DateOnly day)
        => history.LastOrDefault(r => r.EffectiveFrom <= day) ?? history[0];

    public static Costs ToUsd(IEnumerable<CostItem> items, ExchangeRateTable rates)
    {
        var usd = new Dictionary<CostSource, decimal>();
        var unconverted = new Dictionary<(CostSource, string), decimal>();

        foreach (var item in items)
        {
            if (rates.ToUsd(item.Amount, item.Currency, item.Day) is { } amount)
            {
                usd[item.Source] = usd.GetValueOrDefault(item.Source) + amount;
            }
            else
            {
                var key = (item.Source, item.Currency.ToUpperInvariant());
                unconverted[key] = unconverted.GetValueOrDefault(key) + item.Amount;
            }
        }

        return new Costs(
            Round(usd.GetValueOrDefault(CostSource.Labour)),
            Round(usd.GetValueOrDefault(CostSource.Expenses)),
            Round(usd.GetValueOrDefault(CostSource.Materials)),
            Round(usd.GetValueOrDefault(CostSource.GroundLabour)),
            unconverted.Select(u => new UnconvertedCost(u.Key.Item1, u.Key.Item2, Round(u.Value))).ToList());
    }

    /// <summary>
    /// What an Execution client is charged for materials, in USD: each client price converted at the rate
    /// on the day the material was added. Materials with no rate that day are left out and counted.
    /// </summary>
    public static (decimal ChargeUsd, int Unconverted) MaterialCharge(
        IEnumerable<(ExecutionPricing.Material Material, string Currency, DateOnly Day)> materials,
        decimal markup,
        ExchangeRateTable rates)
    {
        var charge = 0m;
        var unconverted = 0;

        foreach (var (material, currency, day) in materials)
        {
            if (rates.ToUsd(ExecutionPricing.ClientPrice(material, markup), currency, day) is { } usd)
            {
                charge += usd;
            }
            else
            {
                unconverted++;
            }
        }

        return (Round(charge), unconverted);
    }

    public static Summary Summarise(Facts facts, Costs costs)
    {
        var revenue = facts.IsClientWork ? facts.Revenue : 0;
        var cost = costs.Total;
        var profit = revenue - cost;
        var hours = facts.LabourMinutes / 60m;
        var marginKnown = facts.IsDone || facts.ProgressPct >= MinProgressForMargin || hours >= MinHoursForMargin;

        Projection? projected = null;
        if (!facts.IsDone && facts.IsClientWork && facts.ProgressPct >= MinProgressForMargin)
        {
            var projectedCost = Round(cost / (facts.ProgressPct / 100m));
            projected = new Projection(projectedCost, revenue - projectedCost, MarginOf(revenue - projectedCost, revenue));
        }

        var progress = facts.IsDone ? 1m : Math.Clamp(facts.ProgressPct, 0, 100) / 100m;

        return new Summary(
            revenue,
            Round(revenue * progress),
            cost,
            profit,
            marginKnown ? MarginOf(profit, revenue) : null,
            marginKnown,
            projected,
            hours > 0 ? Round(cost / hours) : 0);
    }

    private static decimal? MarginOf(decimal profit, decimal revenue)
        => revenue > 0 ? Math.Round(profit / revenue, 4, MidpointRounding.AwayFromZero) : null;

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
