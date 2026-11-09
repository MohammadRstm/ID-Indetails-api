using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.TimeOff;

/// <summary>
/// Time off: a Design employee asks for whole days, from tomorrow and at most 6 months ahead; any super admin approves
/// it, with pay or without, or declines it. The first decision wins.
/// <list type="bullet">
/// <item>Pending: the employee may withdraw it. Still pending when its first day comes, it expires.</item>
/// <item>Approved: a super admin may reject it until its last day. The days before the rejection day stay time off; it
/// and the days after are work days again. Leaving, moving out of Design or becoming an admin ends it the same way.</item>
/// <item>Every calendar day of the period is "off" (no hours, the flag, the alerts); pay counts only its working days,
/// as day excuses: a paid day off costs nothing on a salary and pays an hourly employee their normal day; an unpaid one
/// is deducted as the full paid day.</item>
/// </list>
/// </summary>
public static class TimeOffRules
{
    public const int MaxNoteLength = 500;
    public const int MonthsAhead = 6;

    /// <summary>A request as the rules need it.</summary>
    public sealed record Period(long Id, DateOnly FirstDay, DateOnly LastDay, TimeOffStatus Status, DateOnly? EndedOn, bool? IsPaid)
    {
        /// <summary>The last day taken off; before <see cref="FirstDay"/> when none was (or none was granted).</summary>
        public DateOnly LastTaken => Status switch
        {
            TimeOffStatus.Approved => LastDay,
            TimeOffStatus.Rejected or TimeOffStatus.Ended when EndedOn is { } ended => Min(LastDay, ended.AddDays(-1)),
            _ => FirstDay.AddDays(-1),
        };

        public bool IsOff(DateOnly day) => day >= FirstDay && day <= LastTaken;

        public bool Takes(DateOnly from, DateOnly to) => FirstDay <= to && LastTaken >= from && LastTaken >= FirstDay;
    }

    /// <summary>Still asking, or granted with days to come: what a new request can't overlap.</summary>
    public static bool Holds(TimeOffStatus status) => status is TimeOffStatus.Pending or TimeOffStatus.Approved;

    public static RuleDenial? CheckRequest(DateOnly firstDay, DateOnly lastDay, DateOnly today, bool hasWorkingDay, bool overlapsOwn, bool excused)
    {
        if (firstDay <= today)
        {
            return RuleDenial.Invalid(ErrorCodes.TimeOffTooSoon, "firstDay", "Time off can start tomorrow at the earliest.");
        }

        if (firstDay > today.AddMonths(MonthsAhead))
        {
            return RuleDenial.Invalid(ErrorCodes.TimeOffTooFar, "firstDay", $"Time off can be asked for at most {MonthsAhead} months ahead.");
        }

        if (lastDay < firstDay)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "lastDay", "The last day can't be before the first.");
        }

        if (!hasWorkingDay)
        {
            return RuleDenial.Invalid(ErrorCodes.TimeOffNoWorkingDay, "lastDay", "Those days hold none of your working days.");
        }

        if (overlapsOwn)
        {
            return RuleDenial.BusinessRule(ErrorCodes.TimeOffOverlaps, "You already asked for time off on some of these days.");
        }

        return excused
            ? RuleDenial.BusinessRule(ErrorCodes.TimeOffExcused, "Some of these days are already excused in your hours.")
            : null;
    }

    public static RuleDenial? CheckPending(TimeOffStatus status)
        => status == TimeOffStatus.Pending ? null : RuleDenial.BusinessRule(ErrorCodes.TimeOffDecided, "This request was already decided.");

    /// <summary>Approved before its first day: on that day a request still pending has expired.</summary>
    public static RuleDenial? CheckApprove(TimeOffStatus status, DateOnly firstDay, DateOnly today, bool excused)
    {
        if (CheckPending(status) is { } decided)
        {
            return decided;
        }

        if (firstDay <= today)
        {
            return RuleDenial.BusinessRule(ErrorCodes.TimeOffExpired, "Its first day has come, so it can no longer be approved.");
        }

        return excused
            ? RuleDenial.BusinessRule(ErrorCodes.TimeOffExcused, "Some of these days were excused in the salaries page since it was asked for.")
            : null;
    }

    public static RuleDenial? CheckReject(TimeOffStatus status, DateOnly lastDay, DateOnly today)
    {
        if (status != TimeOffStatus.Approved)
        {
            return RuleDenial.BusinessRule(ErrorCodes.TimeOffNotApproved, "Only approved time off can be rejected.");
        }

        return today > lastDay ? RuleDenial.BusinessRule(ErrorCodes.TimeOffOver, "This time off is over.") : null;
    }

    /// <summary>No hours on a day off, whoever logs them.</summary>
    public static RuleDenial? CheckEntryDays(IEnumerable<DateOnly> days, IReadOnlyList<Period> periods)
    {
        foreach (var day in days.Where(day => periods.Any(p => p.IsOff(day))))
        {
            return RuleDenial.BusinessRule(ErrorCodes.TimeOffDay, $"{day:d MMM yyyy} is a day of time off: no hours can be logged on it.");
        }

        return null;
    }

    /// <summary>While someone is off, they don't change their hours at all.</summary>
    public static RuleDenial? CheckNotOff(DateOnly today, IReadOnlyList<Period> periods)
        => periods.FirstOrDefault(p => p.IsOff(today)) is { } period
            ? RuleDenial.BusinessRule(ErrorCodes.OnTimeOff, $"You're on time off until {period.LastTaken:d MMM yyyy}: hours can't be logged or changed until then.")
            : null;

    /// <summary>A day of time off isn't excused by hand too, nor a month that holds one excused whole: it would count twice.</summary>
    public static RuleDenial? CheckExcuse(ExcuseKind kind, IReadOnlyList<DateOnly> days, IReadOnlyList<Period> periods)
        => periods.Count > 0 && (kind == ExcuseKind.WholeMonth || days.Any(d => periods.Any(p => p.IsOff(d))))
            ? RuleDenial.BusinessRule(ErrorCodes.ExcuseOnTimeOff, kind == ExcuseKind.WholeMonth
                ? "This month holds approved time off, so it can't be excused whole: excuse the other days instead."
                : "Some of these days are approved time off already.")
            : null;

    /// <summary>The period someone is off on <paramref name="day"/>, if any.</summary>
    public static Period? OffOn(DateOnly day, IEnumerable<Period> periods) => periods.FirstOrDefault(p => p.IsOff(day));

    /// <summary>
    /// The month's days taken off, as day excuses for the pay, the hours ledger and the reminders. Every calendar day is
    /// listed: whatever reads them counts only the working days.
    /// </summary>
    public static IReadOnlyList<SalaryCalculator.Excuse> ExcusesIn(DateOnly month, IEnumerable<Period> periods)
    {
        var next = month.AddMonths(1);
        return periods
            .Where(p => p.IsPaid is not null && p.Takes(month, next.AddDays(-1)))
            .Select(p => new SalaryCalculator.Excuse(
                ExcuseKind.Days,
                null,
                DaysBetween(Max(p.FirstDay, month), Min(p.LastTaken, next.AddDays(-1))),
                p.IsPaid!.Value,
                TimeOff: true))
            .ToList();
    }

    public static IReadOnlyList<DateOnly> DaysBetween(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            days.Add(day);
        }

        return days;
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
