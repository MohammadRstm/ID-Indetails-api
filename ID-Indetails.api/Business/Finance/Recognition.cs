using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance;

/// <summary>
/// The months a period touches and how much of each it covers. Revenue needs none of this (it is the money
/// received, counted on the day it arrived; see <see cref="PeriodMoney.Received"/>); company overhead is
/// charged by the calendar days of each month.
/// </summary>
public static class Recognition
{
    public sealed record DatedCost(CostSource Source, DateOnly Day, decimal Usd);

    /// <param name="Calendar">Share of all the month's days that fall in the period: weights overhead.</param>
    public sealed record MonthShare(DateOnly Month, decimal Calendar);

    public static DateOnly MonthOf(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>Every month the period <paramref name="from"/>–<paramref name="to"/> (inclusive) touches.</summary>
    public static IReadOnlyList<MonthShare> Months(DateOnly from, DateOnly to)
    {
        var shares = new List<MonthShare>();
        for (var month = MonthOf(from); month <= to; month = month.AddMonths(1))
        {
            var next = month.AddMonths(1);
            var inside = Days(Max(month, from), Min(next, to.AddDays(1)));
            shares.Add(new MonthShare(month, (decimal)inside / Days(month, next)));
        }

        return shares;
    }

    private static int Days(DateOnly from, DateOnly untilExclusive) => Math.Max(0, untilExclusive.DayNumber - from.DayNumber);

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
