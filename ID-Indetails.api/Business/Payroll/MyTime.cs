using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// A person's month as it stands, on top of <see cref="SalaryCalculator"/> and <see cref="HoursLedger"/>:
/// <list type="bullet">
/// <item>While a salaried month runs, Salaries' "earned so far" is the salary accrued so far plus overtime pay. It accrues with
/// the hours required so far (required so far ÷ the month's) where the hours ledger applies, otherwise with the
/// working days passed. The full-month estimate takes off what would be deducted if the month closed as short as it
/// is now: the shortfall plus the paid break of each whole missed day, at the hourly rate (as Salaries deducts it).
/// Both are estimates: the month's pay is only final once it closes.</item>
/// <item>The ledger history runs from the month they joined to this month (or the month they left), newest first, 12
/// months a page.</item>
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

    public static Estimate EstimateOf(SalaryCalculator.Result pay, SalaryCalculator.Input input, bool inProgress)
    {
        if (!inProgress || pay.Terms.Basis != PayBasis.Monthly)
        {
            return new Estimate(pay.Total, null, 0);
        }

        decimal share, wouldLose = 0;
        if (pay.Ledger is { } ledger)
        {
            var fullMonth = ExpectedFullMonth(input.Month, input.JoinedOn, input.Calendar);
            share = fullMonth > 0 ? Math.Min(1m, (decimal)ledger.Expected / fullMonth) : 1m;
            wouldLose = Math.Min(pay.BasePay, Round(SalaryCalculator.ShortfallMinutes(ledger, input.Calendar) / 60m * pay.HourlyRate));
        }
        else
        {
            share = WorkingDaysPassedShare(input.Month, input.Today, input.JoinedOn, input.Calendar);
        }

        return new Estimate(Round(pay.BasePay * share) + pay.OvertimePay, pay.BasePay + pay.OvertimePay - wouldLose, wouldLose);
    }

    /// <summary>Working days before today ÷ the month's working days, both from the day the person joined.</summary>
    private static decimal WorkingDaysPassedShare(DateOnly month, DateOnly today, DateOnly joinedOn, PersonCalendar calendar)
    {
        var from = joinedOn > month ? joinedOn : month;
        var working = HoursLedger.Days(from, month.AddMonths(1)).Where(d => calendar.HoursOn(d) is not null).ToList();
        return working.Count > 0 ? (decimal)working.Count(d => d < today) / working.Count : 1m;
    }

    /// <summary>One page of months, newest first, from this month (or the month they left) back to the month they joined.</summary>
    public static (IReadOnlyList<DateOnly> Months, int TotalMonths) MonthsPage(DateOnly joinedOn, DateOnly today, int page, DateOnly? leftOn = null)
    {
        var last = leftOn is { } left && left < today ? left : today;
        var current = new DateOnly(last.Year, last.Month, 1);
        var total = Math.Max(0, (current.Year * 12 + current.Month) - (joinedOn.Year * 12 + joinedOn.Month) + 1);
        var months = Enumerable.Range((page - 1) * MonthsPerPage, MonthsPerPage)
            .Where(i => i < total)
            .Select(i => current.AddMonths(-i))
            .ToList();

        return (months, total);
    }

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
