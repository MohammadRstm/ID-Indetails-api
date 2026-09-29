using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance;

/// <summary>
/// When revenue counts. A project's revenue recognised to date (revenue × progress) is spread over the
/// months its work happened: by employee labour cost when it has any, otherwise by its other costs
/// (expenses, materials, ground labour), otherwise by its logged hours, otherwise into the month it was
/// completed. A period then takes each month's share by how much of the month's elapsed days it covers;
/// company overhead is charged by calendar days.
/// </summary>
public static class Recognition
{
    public sealed record DatedCost(CostSource Source, DateOnly Day, decimal Usd);

    /// <param name="Accrued">Share of the month's elapsed days (up to today) that fall in the period: weights revenue.</param>
    /// <param name="Calendar">Share of all the month's days that fall in the period: weights overhead.</param>
    public sealed record MonthShare(DateOnly Month, decimal Accrued, decimal Calendar);

    public static DateOnly MonthOf(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>Every month the period <paramref name="from"/>–<paramref name="to"/> (inclusive) touches.</summary>
    public static IReadOnlyList<MonthShare> Months(DateOnly from, DateOnly to, DateOnly today)
    {
        var shares = new List<MonthShare>();
        for (var month = MonthOf(from); month <= to; month = month.AddMonths(1))
        {
            var next = month.AddMonths(1);
            var inside = Days(Max(month, from), Min(next, to.AddDays(1)));
            var elapsed = Days(month, Min(next, today.AddDays(1)));

            shares.Add(new MonthShare(
                month,
                elapsed > 0 ? Math.Min(1m, (decimal)inside / elapsed) : 0,
                (decimal)inside / Days(month, next)));
        }

        return shares;
    }

    /// <summary>
    /// A project's recognised revenue spread over the months of its cost drivers. Without any priced cost (work only
    /// by people with no pay, for instance), it follows the hours logged, and without those the completion month, so
    /// the revenue always lands in some month.
    /// </summary>
    /// <param name="logged">Minutes logged on the project, by day.</param>
    /// <param name="completedOn">When it was completed or finalised, if it was.</param>
    public static IReadOnlyDictionary<DateOnly, decimal> ByMonth(
        decimal recognisedToDate,
        IReadOnlyCollection<DatedCost> costs,
        IReadOnlyCollection<(DateOnly Day, int Minutes)> logged,
        DateOnly? completedOn)
    {
        if (recognisedToDate == 0)
        {
            return new Dictionary<DateOnly, decimal>();
        }

        var labour = costs.Where(c => c.Source == CostSource.Labour).ToList();
        var drivers = labour.Sum(c => c.Usd) > 0 ? labour : costs.Where(c => c.Source != CostSource.Labour).ToList();
        if (drivers.Sum(c => c.Usd) > 0)
        {
            return Spread(recognisedToDate, drivers.Select(c => (c.Day, c.Usd)).ToList());
        }

        if (logged.Sum(w => w.Minutes) > 0)
        {
            return Spread(recognisedToDate, logged.Select(w => (w.Day, (decimal)w.Minutes)).ToList());
        }

        return completedOn is { } day
            ? new Dictionary<DateOnly, decimal> { [MonthOf(day)] = recognisedToDate }
            : new Dictionary<DateOnly, decimal>();
    }

    private static Dictionary<DateOnly, decimal> Spread(decimal amount, IReadOnlyCollection<(DateOnly Day, decimal Weight)> weights)
    {
        var total = weights.Sum(w => w.Weight);
        return weights.GroupBy(w => MonthOf(w.Day)).ToDictionary(g => g.Key, g => amount * g.Sum(w => w.Weight) / total);
    }

    private static int Days(DateOnly from, DateOnly untilExclusive) => Math.Max(0, untilExclusive.DayNumber - from.DayNumber);

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
