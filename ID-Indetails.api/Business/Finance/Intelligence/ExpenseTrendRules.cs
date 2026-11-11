using System.Globalization;
using ID_Indetails.api.Business.SiteExpenses;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance.Intelligence;

/// <summary>
/// A domain's spending by category, month by month, in USD at the rate of each expense's day, and the owner's rules for
/// calling it unusual (Q16): a category's month far above its average of the months before, an item bought at a unit
/// cost far above its last purchases, and the same item, amount and currency entered twice on one project. Expenses
/// with no exchange rate on their day are left out of every USD figure and listed apart.
/// </summary>
public static class ExpenseTrendRules
{
    /// <param name="BaselineMonths">The months before a category's month that its average is taken over.</param>
    /// <param name="HighRatio">A month is unusual at this many times its average...</param>
    /// <param name="HighUsd">...when it also reaches this many USD.</param>
    /// <param name="UnitCostJumpPct">A unit cost is unusual this many percent above the average of...</param>
    /// <param name="UnitCostPurchases">...this many earlier purchases of the item.</param>
    /// <param name="DuplicateDays">Expenses this many days apart or less may be one typed twice; 0 is the same day.</param>
    public sealed record Thresholds(int BaselineMonths, decimal HighRatio, decimal HighUsd, int UnitCostJumpPct, int UnitCostPurchases, int DuplicateDays);

    /// <param name="Item">Its label or item, as typed.</param>
    /// <param name="Amount">In its own currency.</param>
    /// <param name="Usd">At the rate of its day; null when there was none.</param>
    /// <param name="Unit">A site expense's unit, when it was entered as a quantity × a unit cost.</param>
    /// <param name="UnitCostUsd">The same: its cost ÷ its quantity, in USD; null otherwise.</param>
    public sealed record Expense(
        long Id,
        long ProjectId,
        string Item,
        string Category,
        string Currency,
        decimal Amount,
        decimal? Usd,
        DateOnly SpentOn,
        string? Unit,
        decimal? UnitCostUsd);

    /// <param name="Baseline">The category's average over the months before, months with nothing counting as 0; null while there
    /// aren't that many months of history or they were all empty. Rounded to the cent, to show.</param>
    /// <param name="BaselineTotal">Those months' total, unrounded, so the threshold is judged on the exact average.</param>
    public sealed record CategoryMonth(string Category, decimal Usd, decimal? Baseline, decimal? BaselineTotal = null);

    public sealed record Month(DateOnly Start, decimal Total, IReadOnlyList<CategoryMonth> Categories);

    /// <param name="On">The month (a category's) or the day (an item's) it is about.</param>
    /// <param name="Usd">The month's total, the expense's cost, or one of the matching expenses' cost; null with no exchange rate.</param>
    /// <param name="Baseline">The average it was judged by: the category's month or the item's unit cost.</param>
    /// <param name="Pct">How far above that average, as a whole percentage.</param>
    /// <param name="Weight">Orders anomalies of one kind and day, bigger first.</param>
    public sealed record Anomaly(
        ExpenseAnomalyKind Kind,
        DateOnly On,
        string? Category,
        long? ProjectId,
        string? Item,
        decimal? Usd,
        decimal? Baseline,
        int? Pct,
        IReadOnlyList<long> ExpenseIds,
        decimal Weight,
        string Fact,
        string Rule);

    public static string CategoryHighRule(Thresholds t)
        => $"a category's month at {Ratio(t.HighRatio)}× or more of its average over the {t.BaselineMonths} months before (a month with " +
           $"nothing counts as 0), and at least {Amount(t.HighUsd)} USD";

    public static string UnitCostRule(Thresholds t)
        => $"an item bought at a unit cost {t.UnitCostJumpPct}% or more above the average of its last {t.UnitCostPurchases} purchases, " +
           "matched by item name and unit, in USD at each day's rate";

    public static string DuplicateRule(Thresholds t)
        => "expenses on one project with the same item, amount and currency, " +
           (t.DuplicateDays == 0 ? "on the same day" : $"{t.DuplicateDays} day{(t.DuplicateDays == 1 ? "" : "s")} apart or less") +
           ": maybe one typed twice";

    /// <summary>
    /// Each month of <paramref name="window"/> (oldest first) with every category's USD total and, from the
    /// <see cref="Thresholds.BaselineMonths"/>th month of the domain's history on, its baseline.
    /// </summary>
    /// <param name="all">Every expense of the domain, all time, so the months before the window count.</param>
    public static IReadOnlyList<Month> Months(IReadOnlyList<Expense> all, IReadOnlyList<string> categories, IReadOnlyList<DateOnly> window, Thresholds t)
    {
        var totals = all
            .Where(e => e.Usd is not null)
            .GroupBy(e => (e.Category, Month: Recognition.MonthOf(e.SpentOn)))
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Usd!.Value));
        DateOnly? firstMonth = all.Count == 0 ? null : Recognition.MonthOf(all.Min(e => e.SpentOn));
        decimal UsdOf(string category, DateOnly month) => totals.GetValueOrDefault((category, month));

        decimal? BaselineTotalOf(string category, DateOnly month)
        {
            if (firstMonth is not { } first || month.AddMonths(-t.BaselineMonths) < first)
            {
                return null;
            }

            var total = Enumerable.Range(1, t.BaselineMonths).Sum(i => UsdOf(category, month.AddMonths(-i)));
            return total > 0 ? total : null;
        }

        CategoryMonth CategoryOf(string category, DateOnly month)
        {
            var baselineTotal = BaselineTotalOf(category, month);
            return new CategoryMonth(
                category,
                PeriodMoney.Round(UsdOf(category, month)),
                baselineTotal is { } total ? PeriodMoney.Round(total / t.BaselineMonths) : null,
                baselineTotal);
        }

        return window
            .Select(month => new Month(
                month,
                PeriodMoney.Round(categories.Sum(c => UsdOf(c, month))),
                categories.Select(c => CategoryOf(c, month)).ToList()))
            .ToList();
    }

    /// <summary>Every unusual thing in <paramref name="months"/> and from <paramref name="from"/> on, newest first.</summary>
    /// <param name="all">Every expense of the domain, all time: an item's earlier purchases count from before the window.</param>
    public static IReadOnlyList<Anomaly> Anomalies(IReadOnlyList<Month> months, IReadOnlyList<Expense> all, DateOnly from, Thresholds t)
        => CategoryHighs(months, t)
            .Concat(UnitCostJumps(all, from, t))
            .Concat(Duplicates(all, from, t))
            .OrderByDescending(a => a.On)
            .ThenBy(a => a.Kind)
            .ThenByDescending(a => a.Weight)
            .ToList();

    public static IEnumerable<Anomaly> CategoryHighs(IReadOnlyList<Month> months, Thresholds t)
    {
        foreach (var month in months)
        {
            foreach (var c in month.Categories)
            {
                // month ≥ ratio × (total ÷ months), multiplied out so no rounded average decides it
                if (c.Baseline is not { } b
                    || c.Usd * t.BaselineMonths < t.HighRatio * (c.BaselineTotal ?? b * t.BaselineMonths)
                    || c.Usd < t.HighUsd)
                {
                    continue;
                }

                yield return new Anomaly(
                    ExpenseAnomalyKind.CategoryHigh,
                    month.Start,
                    c.Category,
                    null,
                    null,
                    c.Usd,
                    b,
                    PctAbove(c.Usd, b),
                    [],
                    c.Usd - b,
                    $"{CategoryName(c.Category)}: {Amount(c.Usd)} USD in {month.Start.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}, " +
                    $"{Ratio(c.Usd / b)}× its {t.BaselineMonths}-month average of {Amount(b)} USD.",
                    CategoryHighRule(t));
            }
        }
    }

    public static IEnumerable<Anomaly> UnitCostJumps(IReadOnlyList<Expense> all, DateOnly from, Thresholds t)
    {
        var items = all
            .Where(e => e.UnitCostUsd is > 0)
            .GroupBy(e => (Item: SiteExpenseRules.GroupKey(e.Item), Unit: SiteExpenseRules.GroupKey(e.Unit ?? "")));
        foreach (var item in items)
        {
            var bought = item.OrderBy(e => e.SpentOn).ThenBy(e => e.Id).ToList();
            for (var i = t.UnitCostPurchases; i < bought.Count; i++)
            {
                var expense = bought[i];
                if (expense.SpentOn < from)
                {
                    continue;
                }

                var unitCost = expense.UnitCostUsd!.Value;
                var average = bought.Skip(i - t.UnitCostPurchases).Take(t.UnitCostPurchases).Average(e => e.UnitCostUsd!.Value);
                var pct = (unitCost - average) / average * 100;
                if (pct < t.UnitCostJumpPct)
                {
                    continue;
                }

                var unit = string.IsNullOrWhiteSpace(expense.Unit) ? "unit" : expense.Unit.Trim();
                yield return new Anomaly(
                    ExpenseAnomalyKind.UnitCostJump,
                    expense.SpentOn,
                    expense.Category,
                    expense.ProjectId,
                    expense.Item,
                    expense.Usd,
                    UnitCost(average),
                    PctAbove(unitCost, average),
                    [expense.Id],
                    pct,
                    $"\"{expense.Item}\" at {Amount(UnitCost(unitCost))} USD a {unit} on {Day(expense.SpentOn)}, {PctAbove(unitCost, average)}% above " +
                    $"the average of its last {t.UnitCostPurchases} purchases ({Amount(UnitCost(average))} USD).",
                    UnitCostRule(t));
            }
        }
    }

    public static IEnumerable<Anomaly> Duplicates(IReadOnlyList<Expense> all, DateOnly from, Thresholds t)
    {
        var alike = all.GroupBy(e => (e.ProjectId, Item: SiteExpenseRules.GroupKey(e.Item), e.Amount, Currency: e.Currency.ToUpperInvariant()));
        foreach (var group in alike)
        {
            var runs = new List<List<Expense>>();
            foreach (var expense in group.OrderBy(e => e.SpentOn).ThenBy(e => e.Id))
            {
                if (runs.Count > 0 && expense.SpentOn.DayNumber - runs[^1][^1].SpentOn.DayNumber <= t.DuplicateDays)
                {
                    runs[^1].Add(expense);
                }
                else
                {
                    runs.Add([expense]);
                }
            }

            foreach (var run in runs.Where(r => r.Count > 1 && r[^1].SpentOn >= from))
            {
                var first = run[0];
                var last = run[^1];
                var when = first.SpentOn == last.SpentOn ? $"on {Day(first.SpentOn)}" : $"from {Day(first.SpentOn)} to {Day(last.SpentOn)}";
                yield return new Anomaly(
                    ExpenseAnomalyKind.SameAmountTwice,
                    last.SpentOn,
                    first.Category,
                    first.ProjectId,
                    first.Item,
                    first.Usd,
                    null,
                    null,
                    run.Select(e => e.Id).ToList(),
                    run.Count,
                    $"{run.Count} expenses of {Amount(first.Amount)} {first.Currency} for \"{first.Item}\" {when}.",
                    DuplicateRule(t));
            }
        }
    }

    /// <summary>A category's code as words: "site_expense" reads "Site expense".</summary>
    public static string CategoryName(string category)
    {
        var words = category.Replace('_', ' ');
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static int PctAbove(decimal value, decimal average) => (int)Math.Round((value - average) / average * 100, MidpointRounding.AwayFromZero);

    private static decimal UnitCost(decimal usd) => Math.Round(usd, 4, MidpointRounding.AwayFromZero);

    private static string Amount(decimal amount) => amount.ToString("#,0.####", CultureInfo.InvariantCulture);

    private static string Ratio(decimal ratio) => ratio.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
