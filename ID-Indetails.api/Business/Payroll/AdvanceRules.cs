using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// Salary advances: part of someone's pay handed over early. An advance costs the company nothing more: what a person
/// earns, the hourly rate, project cost and the company overview never change. It only comes off what the person is
/// handed later.
/// <list type="bullet">
/// <item>Anyone with pay asks; a super admin approves it (setting the months it comes off) or declines it, or records one
/// directly. Nobody decides their own, and a super admin's needs someone who may change super admins.</item>
/// <item>At most a month's pay, and no month's repayment above it. One at a time: a new one waits until the last one's
/// months have closed with nothing left.</item>
/// <item>Each month takes what is planned for it plus what earlier months couldn't, but never more than that month's pay;
/// the rest moves on to the next month. In a month the person's employment ends, everything left comes due.</item>
/// <item>Settling closes what is left after the month it is settled in: that month still takes its share (a leaver's
/// final pay first), and nothing comes off pay after it. The rest is repaid in cash, or written off as a company
/// expense on its day.</item>
/// </list>
/// </summary>
public static class AdvanceRules
{
    public const int MaxNoteLength = 500;

    /// <summary>Repayment months run from the first month allowed to at most this many months on.</summary>
    public const int MaxMonths = 12;

    /// <summary>An advance as the rules need it: its amount, what each month plans to take, and when and for how much it was settled.</summary>
    public sealed record Advance(
        long Id,
        decimal Amount,
        IReadOnlyDictionary<DateOnly, decimal> Planned,
        DateOnly? SettledOn,
        decimal? SettledAmount = null);

    /// <param name="Due">What the month had to take: its plan plus what earlier months couldn't, or everything left in a leaving month.</param>
    /// <param name="Taken">What came off the month's pay: what was due, but never more than the pay.</param>
    public sealed record MonthTaken(DateOnly Month, decimal Planned, decimal Due, decimal Taken);

    /// <param name="Left">What is still to be paid back after the months worked out.</param>
    /// <param name="Missing">Months something was due in with no pay given for them, worked out as no pay. Give them and ask again.</param>
    public sealed record Schedule(IReadOnlyList<MonthTaken> Months, decimal Left, IReadOnlyList<DateOnly> Missing)
    {
        public decimal TakenIn(DateOnly month) => Months.Where(m => m.Month == month).Sum(m => m.Taken);

        public decimal TakenBefore(DateOnly month) => Months.Where(m => m.Month < month).Sum(m => m.Taken);
    }

    public sealed record PlannedMonth(DateOnly Month, decimal Amount);

    public static DateOnly MonthOf(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>
    /// How one person's advances come off their pay, month by month up to <paramref name="through"/>. They are taken
    /// oldest first: an earlier advance takes from a month's pay before a later one.
    /// </summary>
    /// <param name="pay">What each month's pay can give, in the advances' currency: 0 for a month paid in another currency or not employed.</param>
    /// <param name="leavingMonths">The months the person's employment ended in.</param>
    public static IReadOnlyDictionary<long, Schedule> Repayments(
        IReadOnlyList<Advance> advances,
        IReadOnlyDictionary<DateOnly, decimal> pay,
        IReadOnlySet<DateOnly> leavingMonths,
        DateOnly through)
    {
        var available = pay.ToDictionary(p => p.Key, p => Math.Max(0, Math.Floor(p.Value * 100) / 100));
        var schedules = new Dictionary<long, Schedule>();
        foreach (var advance in advances)
        {
            var schedule = RepaymentsOf(advance, available, leavingMonths, through);
            foreach (var month in schedule.Months.Where(m => available.ContainsKey(m.Month)))
            {
                available[month.Month] -= month.Taken;
            }

            schedules[advance.Id] = schedule;
        }

        return schedules;
    }

    private static Schedule RepaymentsOf(
        Advance advance,
        IReadOnlyDictionary<DateOnly, decimal> available,
        IReadOnlySet<DateOnly> leavingMonths,
        DateOnly through)
    {
        var months = new List<MonthTaken>();
        var missing = new List<DateOnly>();
        var left = advance.Amount;
        if (advance.Planned.Count == 0)
        {
            return new Schedule(months, left, missing);
        }

        decimal carried = 0;
        for (var month = advance.Planned.Keys.Min(); month <= through && left > 0; month = month.AddMonths(1))
        {
            var settledMonth = advance.SettledOn is { } settledOn ? MonthOf(settledOn) : (DateOnly?)null;
            if (month > settledMonth)
            {
                break;
            }

            var planned = advance.Planned.GetValueOrDefault(month);
            var due = leavingMonths.Contains(month) ? left : Math.Min(left, carried + planned);
            if (month == settledMonth)
            {
                // the month it was settled in takes no more than what the settlement left to it
                due = Math.Min(due, Math.Max(0, left - (advance.SettledAmount ?? 0)));
            }

            if (due <= 0)
            {
                continue;
            }

            if (!available.TryGetValue(month, out var pay))
            {
                missing.Add(month);
                pay = 0;
            }

            var taken = Math.Min(due, pay);
            left -= taken;
            carried = due - taken;
            months.Add(new MonthTaken(month, planned, due, taken));
        }

        return new Schedule(months, left, missing);
    }

    /// <summary>Given and not settled, with something left once the months before this one are counted.</summary>
    public static bool IsOpen(SalaryAdvanceStatus status, DateOnly? settledOn, decimal amount, decimal takenBeforeThisMonth)
        => status == SalaryAdvanceStatus.Approved && settledOn is null && amount - takenBeforeThisMonth > 0;

    public static RuleDenial? CheckAsk(bool hasPay, bool hasPending, bool hasOpen)
    {
        if (!hasPay)
        {
            return RuleDenial.BusinessRule(ErrorCodes.SalaryAdvanceNoPay, "There's no pay set for you yet, so you can't ask for an advance.");
        }

        if (hasPending)
        {
            return RuleDenial.BusinessRule(ErrorCodes.SalaryAdvancePending, "You already have a request waiting for a decision.");
        }

        return hasOpen
            ? RuleDenial.BusinessRule(ErrorCodes.SalaryAdvanceOpen, "Your last advance isn't paid back yet: you can ask again once it is.")
            : null;
    }

    /// <summary>Nobody gives or decides their own advance, and a super admin's needs someone who may change super admins.</summary>
    public static RuleDenial? CheckDecider(long actorId, long personId, bool personIsSuperAdmin, bool actorChangesSuperAdmins)
    {
        if (actorId == personId)
        {
            return RuleDenial.Forbidden(ErrorCodes.EmployeeChangeNotAllowed, "You can't give or decide your own salary advance.");
        }

        return personIsSuperAdmin && !actorChangesSuperAdmins
            ? RuleDenial.Forbidden(ErrorCodes.Forbidden, "Only someone who may change super admins gives or decides a super admin's advance.")
            : null;
    }

    public static RuleDenial? CheckPending(SalaryAdvanceStatus status)
        => status == SalaryAdvanceStatus.Pending
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.SalaryAdvanceDecided, "This request has already been decided or withdrawn.");

    /// <summary>Giving an advance, by approving a request or recording one: how much, and the months it comes off.</summary>
    /// <param name="asked">What the person asked for; null when it is recorded directly.</param>
    /// <param name="monthlyPay">The person's pay for a month, as Salaries shows it: the salary, or an hourly rate × the month's paid hours.</param>
    /// <param name="hasPay">Whether the person has pay set today.</param>
    /// <param name="firstMonth">The earliest month it may come off: the month it is given in.</param>
    public static RuleDenial? CheckGive(
        decimal amount,
        decimal? asked,
        bool hasPay,
        decimal monthlyPay,
        bool hasOpen,
        IReadOnlyList<PlannedMonth> months,
        DateOnly firstMonth)
    {
        if (asked is { } most && amount > most)
        {
            return RuleDenial.Invalid(ErrorCodes.SalaryAdvanceMoreThanAsked, "amount", "An advance can't be more than was asked for.");
        }

        if (!hasPay)
        {
            return RuleDenial.BusinessRule(ErrorCodes.SalaryAdvanceNoPay, "They have no pay set today, so there's nothing an advance could come off.");
        }

        if (hasOpen)
        {
            return RuleDenial.BusinessRule(ErrorCodes.SalaryAdvanceOpen, "Their last advance isn't paid back yet: a new one waits until it is.");
        }

        return amount > monthlyPay
            ? RuleDenial.BusinessRule(ErrorCodes.SalaryAdvanceOverPay, "An advance can't be more than a month's pay.")
            : CheckMonths(months, amount, monthlyPay, firstMonth);
    }

    /// <param name="toPlan">What the months must add up to.</param>
    /// <param name="firstMonth">The earliest month allowed; the months end within <see cref="MaxMonths"/> months of it.</param>
    public static RuleDenial? CheckMonths(IReadOnlyList<PlannedMonth> months, decimal toPlan, decimal monthlyPay, DateOnly firstMonth)
    {
        if (months.Count == 0 || months.Count > MaxMonths)
        {
            return RuleDenial.Invalid(ErrorCodes.SalaryAdvanceMonthsInvalid, "months", $"Choose from 1 to {MaxMonths} months to pay it back in.");
        }

        if (months.Any(m => m.Month.Day != 1 || m.Month < firstMonth || m.Month >= firstMonth.AddMonths(MaxMonths)))
        {
            return RuleDenial.Invalid(
                ErrorCodes.SalaryAdvanceMonthsInvalid, "months", $"Repayments start this month at the earliest and end within {MaxMonths} months.");
        }

        if (months.Select(m => m.Month).Distinct().Count() != months.Count)
        {
            return RuleDenial.Invalid(ErrorCodes.SalaryAdvanceMonthsInvalid, "months", "Each month can be chosen once.");
        }

        if (months.Any(m => m.Amount <= 0 || decimal.Round(m.Amount, 2) != m.Amount))
        {
            return RuleDenial.Invalid(ErrorCodes.SalaryAdvanceMonthsInvalid, "months", "Each month's repayment must be more than zero, to the cent.");
        }

        if (months.Any(m => m.Amount > monthlyPay))
        {
            return RuleDenial.BusinessRule(ErrorCodes.SalaryAdvanceOverPay, "A month's repayment can't be more than a month's pay.");
        }

        return months.Sum(m => m.Amount) == toPlan
            ? null
            : RuleDenial.Invalid(ErrorCodes.SalaryAdvanceMonthsTotal, "months", "The months must add up to what is to be paid back.");
    }

    /// <summary>A recorded advance was handed over this month, up to today.</summary>
    public static RuleDenial? CheckGivenOn(DateOnly givenOn, DateOnly today)
        => givenOn >= MonthOf(today) && givenOn <= today
            ? null
            : RuleDenial.Invalid(ErrorCodes.SalaryAdvanceGivenOn, "givenOn", "An advance is given from the first of this month up to today.");

    /// <summary>Only a given advance that isn't settled can have its months changed, be cancelled or settled.</summary>
    public static RuleDenial? CheckGiven(SalaryAdvanceStatus status, DateOnly? settledOn)
        => status == SalaryAdvanceStatus.Approved && settledOn is null
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.SalaryAdvanceClosed, "This advance is settled, cancelled or wasn't given, so it can't be changed.");

    /// <summary>Changing the months: the open ones (this month on) must add up to what the closed ones don't plan.</summary>
    /// <param name="plannedInClosedMonths">What the months before this one planned to take.</param>
    public static RuleDenial? CheckChangeMonths(
        SalaryAdvanceStatus status,
        DateOnly? settledOn,
        decimal amount,
        decimal plannedInClosedMonths,
        decimal monthlyPay,
        IReadOnlyList<PlannedMonth> months,
        DateOnly currentMonth)
    {
        if (CheckGiven(status, settledOn) is { } denied)
        {
            return denied;
        }

        var toPlan = amount - plannedInClosedMonths;
        return toPlan <= 0
            ? RuleDenial.BusinessRule(
                ErrorCodes.SalaryAdvanceNothingLeft,
                "Every month planned for this advance has closed. Anything they couldn't take moves on by itself, or can be settled.")
            : CheckMonths(months, toPlan, monthlyPay, currentMonth);
    }

    /// <summary>An advance given by mistake is cancelled while the month it was given in is still open.</summary>
    public static RuleDenial? CheckCancel(SalaryAdvanceStatus status, DateOnly? settledOn, DateOnly? givenOn, DateOnly currentMonth)
    {
        if (CheckGiven(status, settledOn) is { } denied)
        {
            return denied;
        }

        return givenOn is { } given && MonthOf(given) == currentMonth
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.SalaryAdvanceCancelClosed, "An advance can be cancelled only in the month it was given.");
    }

    /// <param name="left">What is left once the months before this one are counted.</param>
    public static RuleDenial? CheckSettle(SalaryAdvanceStatus status, DateOnly? settledOn, decimal left)
    {
        if (CheckGiven(status, settledOn) is { } denied)
        {
            return denied;
        }

        return left > 0
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.SalaryAdvanceNothingLeft, "Nothing of this advance is left to settle.");
    }
}
