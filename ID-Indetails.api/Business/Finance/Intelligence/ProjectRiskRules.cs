using System.Globalization;
using ID_Indetails.api.Business.Labour;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance.Intelligence;

/// <summary>
/// The financial risks of one project, each from a stated rule: the profit floor's standing, the straight-line
/// projection, refused changes still waiting, Design departments past their budget, a site's money running out (the
/// site money timeline's projection), costs with no exchange rate, and a settlement that ended at zero or below (Q10).
/// Every figure comes from the shared calculator; nothing here judges a person.
/// </summary>
public static class ProjectRiskRules
{
    /// <param name="RunwayWorkingDays">A site's money is short when it runs out within this many working days.</param>
    /// <param name="SettlementMonths">How far back a settlement on a finished project is listed.</param>
    public sealed record Thresholds(int RunwayWorkingDays, int SettlementMonths);

    /// <summary>A Design department's budget and labour cost, in USD, without its people.</summary>
    public sealed record Department(string Name, decimal Budget, decimal Cost);

    /// <param name="ProfitTaken">What the settlement took off the agreed profit, in USD net of TVA (positive).</param>
    public sealed record Settlement(OwedSettlement As, decimal ProfitTaken, DateOnly CompletedOn);

    /// <param name="Open">Active or on hold: only these get the risks of a running project.</param>
    /// <param name="FloorApplies">A client project with an agreed amount (<see cref="ProfitFloorRules.Applies"/>).</param>
    /// <param name="AgreedRevenue">USD net of TVA.</param>
    /// <param name="AgreedProfit">Agreed revenue − all cost so far (+ a site's pocket money), USD net of TVA.</param>
    /// <param name="Projected">The calculator's straight-line projection, while the project is at least 5% along.</param>
    /// <param name="PendingChanges">When each refused change still waiting was asked for.</param>
    /// <param name="Runway">A running site's site money projection; null elsewhere.</param>
    /// <param name="SiteCashProfit">A site's cash profit, the figure <paramref name="Runway"/> projects.</param>
    /// <param name="Settled">A finished project settled within the lookback; null otherwise.</param>
    public sealed record Facts(
        bool Open,
        bool FloorApplies,
        decimal AgreedRevenue,
        decimal AgreedProfit,
        ProjectFinance.Projection? Projected,
        int ProgressPct,
        IReadOnlyList<Department> Departments,
        IReadOnlyList<DateOnly> PendingChanges,
        IReadOnlyList<ProjectFinance.UnconvertedCost> Unconverted,
        LabourRules.Projection? Runway,
        decimal? SiteCashProfit,
        Settlement? Settled);

    /// <param name="Weight">Orders risks of one kind, bigger first.</param>
    /// <param name="Amount">The USD figure the risk is about, where there is one.</param>
    /// <param name="Fact">What was measured, in words.</param>
    /// <param name="Rule">The rule that fired, in words.</param>
    public sealed record Risk(ProjectRiskKind Kind, decimal Weight, decimal? Amount, string Fact, string Rule);

    public const string ProfitAtZeroRule = "an agreed profit of 0 or below, against the profit floor";

    public static string ProfitCloseRule { get; } = $"a margin of {Pct(ProfitFloorRules.WarnMargin)} or less, against the profit floor";

    public const string ProjectedLossRule =
        "the cost so far ÷ progress (requirements done) puts the profit at completion at 0 or below: a straight-line estimate, made from 5% along";

    public const string BlockedChangeRule = "a change the profit floor refused, waiting to be forced or dismissed";

    public const string DepartmentRule = "a department's labour cost past its budget (its share of the price, plus its variation orders)";

    public const string UnconvertedRule = "costs with no exchange rate on their day, left out of cost and profit";

    public static string RunwayRule(Thresholds t)
        => $"a running site whose cash profit is already below 0, or an active one that the site money timeline sees run out within " +
           $"{t.RunwayWorkingDays} working days at today's crew and day rate";

    public static string SettlementRule(Thresholds t)
        => $"finished in the last {t.SettlementMonths} months with what the client owed excused or written off, ending at an agreed profit " +
           "of 0 or below (a settlement isn't held to the profit floor)";

    public static IReadOnlyList<Risk> Of(Facts f, Thresholds t, DateOnly today)
    {
        var risks = new List<Risk>();
        if (f.Open)
        {
            AddRunningRisks(f, t, today, risks);
        }

        if (f.Settled is { } s && f.AgreedProfit <= 0)
        {
            var how = s.As == OwedSettlement.Excused ? "Excused" : "Written off";
            risks.Add(new Risk(ProjectRiskKind.SettlementAtZero, s.ProfitTaken, s.ProfitTaken,
                $"{how} at completion on {Day(s.CompletedOn)}, taking {Usd(s.ProfitTaken)} off the profit: an agreed profit of {Usd(f.AgreedProfit)}.",
                SettlementRule(t)));
        }

        return risks;
    }

    /// <summary>
    /// One entry per project with risks, its risks in ranking order; projects ranked by their most pressing risk, then its
    /// weight, then id.
    /// </summary>
    public static IReadOnlyList<(long ProjectId, IReadOnlyList<Risk> Risks)> Rank(IEnumerable<(long ProjectId, IReadOnlyList<Risk> Risks)> projects)
        => projects
            .Where(p => p.Risks.Count > 0)
            .Select(p => (p.ProjectId, Risks: (IReadOnlyList<Risk>)p.Risks.OrderBy(r => r.Kind).ThenByDescending(r => r.Weight).ToList()))
            .OrderBy(p => p.Risks[0].Kind)
            .ThenByDescending(p => p.Risks[0].Weight)
            .ThenBy(p => p.ProjectId)
            .ToList();

    private static void AddRunningRisks(Facts f, Thresholds t, DateOnly today, List<Risk> risks)
    {
        if (f.FloorApplies)
        {
            var standing = ProfitFloorRules.StandingOf(f.AgreedProfit, f.AgreedRevenue);
            if (standing == ProfitFloorRules.Standing.AtZero)
            {
                risks.Add(new Risk(ProjectRiskKind.ProfitAtZero, -f.AgreedProfit, f.AgreedProfit,
                    $"An agreed profit of {Usd(f.AgreedProfit)} on {Usd(f.AgreedRevenue)} agreed.", ProfitAtZeroRule));
            }
            else
            {
                // at zero already says more than a projection of a loss
                if (f.Projected is { Profit: <= 0 } projected)
                {
                    risks.Add(new Risk(ProjectRiskKind.ProjectedLoss, -projected.Profit, projected.Profit,
                        $"At {f.ProgressPct}% along, the cost so far points to {Usd(projected.Cost)} at completion: a profit of " +
                        $"{Usd(projected.Profit)} on {Usd(f.AgreedRevenue)} agreed.",
                        ProjectedLossRule));
                }

                if (standing == ProfitFloorRules.Standing.Close)
                {
                    var margin = f.AgreedProfit / f.AgreedRevenue;
                    risks.Add(new Risk(ProjectRiskKind.ProfitClose, -margin, f.AgreedProfit,
                        $"An agreed profit of {Usd(f.AgreedProfit)} on {Usd(f.AgreedRevenue)} agreed, a margin of {Pct(margin)}.", ProfitCloseRule));
                }
            }
        }

        if (f.Runway is { } runway)
        {
            var cash = f.SiteCashProfit ?? 0;
            if (runway.State == LabourRules.ProjectionState.Losing)
            {
                // spent past what the client paid comes before any countdown
                risks.Add(new Risk(ProjectRiskKind.ShortRunway, 1 - cash, cash,
                    $"A cash profit of {Usd(cash)}: the site has spent more than the client paid.", RunwayRule(t)));
            }
            else if (runway is { State: LabourRules.ProjectionState.Ok, WorkingDays: { } days, ZeroOn: { } zeroOn } && days <= t.RunwayWorkingDays)
            {
                risks.Add(new Risk(ProjectRiskKind.ShortRunway, -days, cash,
                    $"A cash profit of {Usd(cash)} runs out in {days} working day{(days == 1 ? "" : "s")}, on {Day(zeroOn)}, at {Usd(runway.DailyCost)} a day.",
                    RunwayRule(t)));
            }
        }

        if (f.PendingChanges.Count > 0)
        {
            var oldest = f.PendingChanges.Min();
            var waiting = f.PendingChanges.Count == 1
                ? $"A refused change waiting since {Day(oldest)}."
                : $"{f.PendingChanges.Count} refused changes waiting, the oldest since {Day(oldest)}.";
            risks.Add(new Risk(ProjectRiskKind.BlockedChange, today.DayNumber - oldest.DayNumber, null, waiting, BlockedChangeRule));
        }

        foreach (var department in f.Departments.Where(d => d.Cost > d.Budget))
        {
            var over = department.Cost - department.Budget;
            risks.Add(new Risk(ProjectRiskKind.DepartmentOverBudget, over, over,
                $"{department.Name}: {Usd(department.Cost)} of labour on a budget of {Usd(department.Budget)}, {Usd(over)} over.", DepartmentRule));
        }

        if (f.Unconverted.Count > 0)
        {
            var list = string.Join(", ", f.Unconverted.Select(u => $"{Amount(u.Amount)} {u.Currency} of {SourceName(u.Source)}"));
            risks.Add(new Risk(ProjectRiskKind.UnconvertedCosts, f.Unconverted.Count, null, $"Not counted: {list}.", UnconvertedRule));
        }
    }

    private static string SourceName(CostSource source) => source switch
    {
        CostSource.Labour => "labour",
        CostSource.Expenses => "expenses",
        CostSource.SiteExpense => "site expenses",
        _ => "ground labour",
    };

    private static string Amount(decimal amount) => amount.ToString("#,0.##", CultureInfo.InvariantCulture);

    private static string Usd(decimal amount) => Amount(amount) + " USD";

    private static string Pct(decimal fraction) => (fraction * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
