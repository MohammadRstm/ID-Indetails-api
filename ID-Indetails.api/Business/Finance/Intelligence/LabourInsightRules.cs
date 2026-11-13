using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance.Intelligence;

/// <summary>
/// Design labour cost and hours over a run of months, added up by month, by project and by department, with the part
/// logged outside the agreed scope. The calculator's rows are per person; nothing here keeps a person: every figure is
/// a sum over a month, a project or a department.
/// </summary>
public static class LabourInsightRules
{
    /// <param name="TeamId">The department the hours were logged in; null for none.</param>
    /// <param name="Cost">USD at the rate of each day, paid breaks included.</param>
    /// <param name="OutsideScopeCost">The worked time logged outside the scope (paid breaks stay with the day).</param>
    public sealed record Row(
        long ProjectId,
        long? TeamId,
        DateOnly Month,
        int Minutes,
        decimal Cost,
        int OutsideScopeMinutes,
        decimal OutsideScopeCost,
        IReadOnlyList<ProjectFinance.UnconvertedCost> Unconverted);

    /// <param name="OutsideScopeShare">Outside-scope cost ÷ cost, a fraction; null with no cost.</param>
    public sealed record Totals(decimal Cost, int Minutes, decimal OutsideScopeCost, int OutsideScopeMinutes, decimal? OutsideScopeShare);

    public sealed record Month(DateOnly Start, Totals Totals);

    /// <param name="LabourShare">Its part of the window's labour cost, a fraction; null with no cost.</param>
    public sealed record Part(long? Id, Totals Totals, decimal? LabourShare);

    /// <param name="Months">Every month of the window, oldest first, empty ones included.</param>
    /// <param name="Projects">Projects with labour in the window, the costliest first.</param>
    /// <param name="Departments">Departments with labour in the window, the costliest first; a null id for hours in no department.</param>
    /// <param name="Unconverted">Labour with no exchange rate on its day, per currency: in no cost.</param>
    public sealed record Result(
        Totals Total,
        IReadOnlyList<Month> Months,
        IReadOnlyList<Part> Projects,
        IReadOnlyList<Part> Departments,
        IReadOnlyList<(string Currency, decimal Amount)> Unconverted);

    /// <param name="window">The months to cover, oldest first: rows outside them are left out.</param>
    public static Result Of(IEnumerable<Row> rows, IReadOnlyList<DateOnly> window)
    {
        var months = window.ToHashSet();
        var inWindow = rows.Where(r => months.Contains(r.Month)).ToList();
        var total = TotalsOf(inWindow);

        IReadOnlyList<Part> PartsBy(Func<Row, long?> key)
            => inWindow
                .GroupBy(key)
                .Select(g => (Id: g.Key, Totals: TotalsOf(g)))
                .Where(p => p.Totals.Minutes > 0 || p.Totals.Cost != 0)
                .OrderByDescending(p => p.Totals.Cost)
                .ThenByDescending(p => p.Totals.Minutes)
                .ThenBy(p => p.Id ?? long.MaxValue)
                .Select(p => new Part(p.Id, p.Totals, ShareOf(p.Totals.Cost, total.Cost)))
                .ToList();

        return new Result(
            total,
            window.Select(m => new Month(m, TotalsOf(inWindow.Where(r => r.Month == m)))).ToList(),
            PartsBy(r => r.ProjectId),
            PartsBy(r => r.TeamId),
            inWindow
                .SelectMany(r => r.Unconverted)
                .Where(u => u.Source == CostSource.Labour)
                .GroupBy(u => u.Currency.ToUpperInvariant())
                .OrderBy(g => g.Key)
                .Select(g => (g.Key, g.Sum(u => u.Amount)))
                .ToList());
    }

    private static Totals TotalsOf(IEnumerable<Row> rows)
    {
        var list = rows.ToList();
        var cost = list.Sum(r => r.Cost);
        var outside = list.Sum(r => r.OutsideScopeCost);
        return new Totals(
            PeriodMoney.Round(cost),
            list.Sum(r => r.Minutes),
            PeriodMoney.Round(outside),
            list.Sum(r => r.OutsideScopeMinutes),
            ShareOf(outside, cost));
    }

    private static decimal? ShareOf(decimal part, decimal whole)
        => whole > 0 ? Math.Round(part / whole, 4, MidpointRounding.AwayFromZero) : null;
}
