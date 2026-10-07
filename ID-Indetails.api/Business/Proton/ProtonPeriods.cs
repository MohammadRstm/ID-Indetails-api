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
}
