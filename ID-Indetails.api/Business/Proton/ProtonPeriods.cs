using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Proton;

/// <summary>The periods Proton's tools accept: words like "last month" are turned into days here, never by the model.</summary>
public enum ProtonPeriod
{
    Today,
    Yesterday,
    ThisWeek,
    LastWeek,
    ThisMonth,
    LastMonth,
    ThisQuarter,
    LastQuarter,
    ThisYear,
    LastYear,
    Last7Days,
    Last30Days,
    Last90Days,

    /// <summary>The days given in from and to.</summary>
    Custom,
}

/// <summary>Inclusive company-time-zone days.</summary>
public sealed record DayRange(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
}

public static class ProtonPeriods
{
    /// <summary>
    /// The days of <paramref name="period"/> as of <paramref name="today"/>. Periods that are still running end
    /// today ("this month" is month to date). Custom needs both days, in order, and no longer than
    /// <paramref name="maxDays"/>.
    /// </summary>
    public static (DayRange? Range, RuleDenial? Denial) Resolve(
        ProtonPeriod period, DateOnly? from, DateOnly? to, DateOnly today, DayOfWeek firstDayOfWeek, int maxDays = 366)
    {
        var weekStart = WorkStats.WeekStartOf(today, firstDayOfWeek);
        var monthStart = WorkStats.MonthStartOf(today);
        var quarterStart = WorkStats.QuarterStartOf(today);
        var yearStart = new DateOnly(today.Year, 1, 1);

        var range = period switch
        {
            ProtonPeriod.Today => new DayRange(today, today),
            ProtonPeriod.Yesterday => new DayRange(today.AddDays(-1), today.AddDays(-1)),
            ProtonPeriod.ThisWeek => new DayRange(weekStart, today),
            ProtonPeriod.LastWeek => new DayRange(weekStart.AddDays(-7), weekStart.AddDays(-1)),
            ProtonPeriod.ThisMonth => new DayRange(monthStart, today),
            ProtonPeriod.LastMonth => new DayRange(monthStart.AddMonths(-1), monthStart.AddDays(-1)),
            ProtonPeriod.ThisQuarter => new DayRange(quarterStart, today),
            ProtonPeriod.LastQuarter => new DayRange(quarterStart.AddMonths(-3), quarterStart.AddDays(-1)),
            ProtonPeriod.ThisYear => new DayRange(yearStart, today),
            ProtonPeriod.LastYear => new DayRange(yearStart.AddYears(-1), yearStart.AddDays(-1)),
            ProtonPeriod.Last7Days => new DayRange(today.AddDays(-6), today),
            ProtonPeriod.Last30Days => new DayRange(today.AddDays(-29), today),
            ProtonPeriod.Last90Days => new DayRange(today.AddDays(-89), today),
            _ => null,
        };

        if (range is null)
        {
            if (from is null || to is null)
            {
                return (null, RuleDenial.Invalid(ErrorCodes.ValidationFailed, "from", "A custom period needs both from and to."));
            }

            if (to < from)
            {
                return (null, RuleDenial.Invalid(ErrorCodes.ValidationFailed, "to", "The period ends before it starts."));
            }

            range = new DayRange(from.Value, to.Value);
        }

        return range.Days > maxDays
            ? (null, RuleDenial.Invalid(ErrorCodes.ValidationFailed, "period", $"The period can be at most {maxDays} days."))
            : (range, null);
    }

    /// <summary>
    /// The period a <paramref name="current"/> one is compared with, like for like:
    /// <list type="bullet">
    /// <item>a period still running (this week, month, quarter, year) against the same number of days at the start of
    /// the one before — 1–7 Oct against 1–7 Sep — never past that period's end;</item>
    /// <item>a finished week, month, quarter or year against the whole one before;</item>
    /// <item>a day, a last-N-days span or custom days against as many days just before.</item>
    /// </list>
    /// </summary>
    public static (ProtonPeriodPair? Pair, RuleDenial? Denial) Compare(
        ProtonPeriod period,
        DayRange current,
        ProtonComparison comparison,
        DateOnly? from,
        DateOnly? to,
        int maxDays = 366)
    {
        var partial = Running(period) is { } unitEnd && current.To < unitEnd(current.From);
        switch (comparison)
        {
            case ProtonComparison.None:
                return (new ProtonPeriodPair(current, null, partial, null), null);
            case ProtonComparison.SamePeriodLastYear:
                return (new ProtonPeriodPair(
                    current, new DayRange(current.From.AddYears(-1), current.To.AddYears(-1)), partial, "the same days a year earlier"), null);
            case ProtonComparison.Custom:
                var (custom, denial) = Resolve(ProtonPeriod.Custom, from, to, current.To, DayOfWeek.Monday, maxDays);
                return custom is null ? (null, denial) : (new ProtonPeriodPair(current, custom, partial, "the days asked for"), null);
        }

        var days = current.Days;
        var (previous, note) = period switch
        {
            ProtonPeriod.ThisWeek => (new DayRange(current.From.AddDays(-7), current.From.AddDays(days - 8)), SameStart(partial, "week")),
            ProtonPeriod.ThisMonth => (StartOf(current.From.AddMonths(-1), current.From, days), SameStart(partial, "month")),
            ProtonPeriod.ThisQuarter => (StartOf(current.From.AddMonths(-3), current.From, days), SameStart(partial, "quarter")),
            ProtonPeriod.ThisYear => (StartOf(current.From.AddYears(-1), current.From, days), SameStart(partial, "year")),
            ProtonPeriod.LastWeek => (new DayRange(current.From.AddDays(-7), current.To.AddDays(-7)), "the week before"),
            ProtonPeriod.LastMonth => (new DayRange(current.From.AddMonths(-1), current.From.AddDays(-1)), "the month before"),
            ProtonPeriod.LastQuarter => (new DayRange(current.From.AddMonths(-3), current.From.AddDays(-1)), "the quarter before"),
            ProtonPeriod.LastYear => (new DayRange(current.From.AddYears(-1), current.From.AddDays(-1)), "the year before"),
            _ => (new DayRange(current.From.AddDays(-days), current.From.AddDays(-1)), $"the {days} day{(days == 1 ? "" : "s")} just before"),
        };
        return (new ProtonPeriodPair(current, previous, partial, note), null);
    }

    /// <summary>For a period that runs until the end of its week, month, quarter or year: that end, from its first day.</summary>
    private static Func<DateOnly, DateOnly>? Running(ProtonPeriod period) => period switch
    {
        ProtonPeriod.ThisWeek => start => start.AddDays(6),
        ProtonPeriod.ThisMonth => start => start.AddMonths(1).AddDays(-1),
        ProtonPeriod.ThisQuarter => start => start.AddMonths(3).AddDays(-1),
        ProtonPeriod.ThisYear => start => start.AddYears(1).AddDays(-1),
        _ => null,
    };

    /// <summary>As many days from <paramref name="start"/> as the current period has, ending before <paramref name="currentStart"/> at the latest.</summary>
    private static DayRange StartOf(DateOnly start, DateOnly currentStart, int days)
    {
        var end = start.AddDays(days - 1);
        return new DayRange(start, end < currentStart ? end : currentStart.AddDays(-1));
    }

    private static string SameStart(bool partial, string unit)
        => partial ? $"the same number of days at the start of the {unit} before" : $"the whole {unit} before";
}

public enum ProtonComparison
{
    /// <summary>The period just before, like for like (<see cref="ProtonPeriods.Compare"/>).</summary>
    Previous,
    SamePeriodLastYear,
    /// <summary>The days given in compare_from and compare_to.</summary>
    Custom,
    None,
}

/// <param name="Partial">The current period is still running: its figures are "so far".</param>
/// <param name="Note">How <paramref name="Previous"/> was chosen, in words for the answer.</param>
public sealed record ProtonPeriodPair(DayRange Current, DayRange? Previous, bool Partial, string? Note);
