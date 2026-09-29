using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// What a person sees of their own hours and pay, on top of <see cref="SalaryCalculator"/> and <see cref="HoursLedger"/>:
/// <list type="bullet">
/// <item>Day by day: what each day asked for and what was logged, up to today.</item>
/// <item>While a salaried month runs, "earned so far" is the salary accrued with the hours required so far
/// (required so far ÷ the month's) plus overtime pay, and the full-month estimate takes off what the person is
/// behind at their hourly rate. Both are estimates: the month's pay is only final once it closes.</item>
/// <item>The ledger history runs from the month they joined to this month, newest first, 12 months a page.</item>
/// </list>
/// </summary>
public static class MyTime
{
    public const int MonthsPerPage = 12;

    public enum LedgerStatus
    {
        /// <summary>The whole month is excused.</summary>
        Excused,

        /// <summary>A closed month still short of hours.</summary>
        Short,

        /// <summary>The month in progress is short so far.</summary>
        Behind,

        /// <summary>The month in progress has every required hour covered so far.</summary>
        OnTrack,

        /// <summary>A closed month with every required hour covered.</summary>
        Complete,
    }

    /// <param name="Required">Minutes the day asked for; 0 on a day off or before the person joined.</param>
    /// <param name="Logged">Every minute logged that day.</param>
    public sealed record Day(DateOnly Date, int Required, int Logged, int Overtime, bool Excused, bool IsToday)
    {
        public int Inside => Logged - Overtime;

        public bool DayOff => Required == 0;

        /// <summary>Inside hours − required; null where a day isn't judged (today, excused, a day off).</summary>
        public int? Difference => IsToday || Excused || DayOff ? null : Inside - Required;
    }

    public sealed record Estimate(decimal EarnedSoFar, decimal? FullMonth, decimal WouldLose);

    /// <summary>The required minutes of the whole month, from the day the person joined.</summary>
    public static int ExpectedFullMonth(DateOnly month, DateOnly joinedOn, PersonCalendar calendar)
        => HoursLedger.Days(joinedOn > month ? joinedOn : month, month.AddMonths(1)).Sum(d => calendar.HoursOn(d)?.Required ?? 0);

    public static LedgerStatus StatusOf(HoursLedger.Result ledger, bool wholeMonthExcused)
        => wholeMonthExcused ? LedgerStatus.Excused
            : ledger.Flagged ? LedgerStatus.Short
            : ledger.Behind ? LedgerStatus.Behind
            : ledger.InProgress ? LedgerStatus.OnTrack
            : LedgerStatus.Complete;

    /// <summary>The month's days up to today, newest first, leaving out days off with nothing logged.</summary>
    public static IReadOnlyList<Day> Days(
        DateOnly month,
        DateOnly today,
        DateOnly joinedOn,
        PersonCalendar calendar,
        IReadOnlyList<Overtime.Split> work,
        IReadOnlySet<DateOnly> excusedDays,
        bool wholeMonthExcused)
    {
        var byDay = work.ToLookup(w => w.Day);
        var until = today < month.AddMonths(1) ? today.AddDays(1) : month.AddMonths(1);

        return HoursLedger.Days(month, until)
            .Select(d => new Day(
                d,
                d < joinedOn ? 0 : calendar.HoursOn(d)?.Required ?? 0,
                byDay[d].Sum(w => w.Minutes),
                byDay[d].Sum(w => w.Overtime),
                wholeMonthExcused || excusedDays.Contains(d),
                d == today))
            .Where(d => d.Required > 0 || d.Logged > 0)
            .OrderByDescending(d => d.Date)
            .ToList();
    }

    public static Estimate EstimateOf(SalaryCalculator.Result pay, bool inProgress, int expectedFullMonth)
    {
        if (!inProgress || pay.Terms.Basis != PayBasis.Monthly || pay.Ledger is null || expectedFullMonth <= 0)
        {
            return new Estimate(pay.Total, null, 0);
        }

        var accrued = Round(pay.BasePay * Math.Min(1m, (decimal)pay.Ledger.Expected / expectedFullMonth));
        var wouldLose = Round(pay.Ledger.Short / 60m * pay.Terms.HourlyRate);
        return new Estimate(accrued + pay.OvertimePay, pay.BasePay + pay.OvertimePay - wouldLose, wouldLose);
    }

    /// <summary>One page of months, newest first, from this month back to the month the person joined.</summary>
    public static (IReadOnlyList<DateOnly> Months, int TotalMonths) MonthsPage(DateOnly joinedOn, DateOnly today, int page)
    {
        var current = new DateOnly(today.Year, today.Month, 1);
        var total = Math.Max(0, (current.Year * 12 + current.Month) - (joinedOn.Year * 12 + joinedOn.Month) + 1);
        var months = Enumerable.Range((page - 1) * MonthsPerPage, MonthsPerPage)
            .Where(i => i < total)
            .Select(i => current.AddMonths(-i))
            .ToList();

        return (months, total);
    }

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
