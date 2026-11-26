using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// What someone is owed for a month. Everyone is paid a monthly salary, and pay is per day: each working day they were
/// employed earns the salary in force that day × the day's paid minutes ÷ the whole month's paid minutes. A month on
/// one salary pays exactly that salary; a month with a pay change is split at the day it starts.
/// <list type="bullet">
/// <item>An hour of a day is its salary ÷ the month's paid hours (<see cref="PayRules.HourlyRate"/>). What has a day of
/// its own (work, a day off) is priced at that day's salary. What has none (the hours ledger's shortfall, excused or
/// unpaid minutes) is priced at the month's blended rate, the paid days' rates weighted by their paid hours
/// (<see cref="Result.HourlyRate"/>).</item>
/// <item>The base is pro-rated on the paid hours of the days they were employed if they joined, left or came back
/// mid-month (days away between two periods of employment aren't paid). Overtime is paid at the overtime rate of its
/// day's salary × the multiplier. In a closed month the shortfall is deducted, and each missed working day also loses
/// its paid break, so it costs the full paid day. Unpaid excuses are deducted the same way (an unpaid day off costs the
/// full paid day).</item>
/// <item>A month is paid in one currency, that of the pay in force on its last day (or today, while it runs): a
/// currency change starts on the 1st, and a day on pay in another currency is paid on the month's.</item>
/// <item>From 1 November 2026 (the working time's overtime threshold, 184 hours) a salary's overtime is paid only for the
/// hours counted past the month's threshold, see <see cref="Threshold"/>.</item>
/// </list>
/// </summary>
public static class SalaryCalculator
{
    /// <param name="OvertimeRate">What an hour of overtime is paid at, before the multiplier: stored with the pay.</param>
    /// <param name="Amount">The monthly salary.</param>
    public sealed record PayTerms(DateOnly EffectiveFrom, decimal Amount, string Currency, decimal OvertimeRate)
    {
        public decimal HourlyRateOn(DateOnly day, PersonCalendar calendar) => PayRules.HourlyRate(Amount, day, calendar);
    }

    /// <param name="TimeOff">Days of approved time off rather than an excuse.</param>
    public sealed record Excuse(ExcuseKind Kind, int? Minutes, IReadOnlyList<DateOnly> Days, bool IsPaid, bool TimeOff = false);

    public sealed record Input(
        DateOnly Month,
        DateOnly Today,
        DateOnly JoinedOn,
        bool LedgerApplies,
        IReadOnlyList<PayTerms> PayHistory,
        PersonCalendar Calendar,
        IReadOnlyList<Overtime.Work> Work,
        IReadOnlyList<Excuse> Excuses,
        DateOnly? LeftOn = null,
        IReadOnlyList<Employment.Gap>? Gaps = null,
        bool FixedSalary = false);

    /// <summary>A stretch of the month paid on one salary: a month with a pay change has two or more.</summary>
    /// <param name="From">Its first working day they were employed on.</param>
    /// <param name="To">Its last one.</param>
    /// <param name="Pay">What its days earned, before any deduction.</param>
    public sealed record Part(DateOnly From, DateOnly To, decimal Salary, decimal OvertimeRate, int Days, decimal Pay);

    /// <param name="Terms">The pay in force on the month's last day (or today, while it runs): its salary and currency.</param>
    /// <param name="HourlyRate">The month's blended hourly rate: it prices what has no day of its own (the shortfall, excused minutes).</param>
    /// <param name="OvertimeRate">The overtime rate of <paramref name="Terms"/>.</param>
    /// <param name="OvertimePaidMinutes">The overtime paid as overtime: past the threshold where one applies, otherwise what didn't make up a short month.</param>
    /// <param name="ThresholdMinutes">A salary's overtime is paid only past this many counted minutes; null where no threshold applies.</param>
    /// <param name="CountedMinutes">What counts toward the threshold: every logged minute, the break taken once it counts, and paid excuses; 0 where none applies.</param>
    /// <param name="PaidOvertimeByWork">The overtime paid as overtime, by work entry.</param>
    /// <param name="TimeOffDays">Working days of time off in the month, paid and unpaid.</param>
    /// <param name="Parts">The stretches of the month on one salary, oldest first.</param>
    /// <param name="OvertimeAtHourlyRate">The paid overtime priced at its days' hourly rates, as projects carry it past a threshold.</param>
    public sealed record Result(
        PayTerms Terms,
        decimal HourlyRate,
        decimal OvertimeRate,
        HoursLedger.Result? Ledger,
        IReadOnlyList<Overtime.Split> Work,
        int NormalMinutes,
        int OvertimeMinutes,
        int MadeUpMinutes,
        int OvertimePaidMinutes,
        int ExcusedMinutes,
        bool ProRated,
        int WorkingDays,
        int WorkingDaysPaid,
        int DeductionMinutes,
        decimal Deduction,
        decimal BasePay,
        decimal OvertimePay,
        int? ThresholdMinutes = null,
        int CountedMinutes = 0,
        IReadOnlyDictionary<long, int>? PaidOvertimeByWork = null,
        int TimeOffDays = 0,
        IReadOnlyList<Part>? Parts = null,
        decimal OvertimeAtHourlyRate = 0)
    {
        public decimal Total => BasePay + OvertimePay;
    }

    /// <summary>
    /// When a salary's overtime is paid past a threshold: the working time's hours (184 from 1 November 2026) or the
    /// month's expected hours if those are more, so overtime is only paid once the month's own hours are done. It is
    /// fixed: joining or leaving mid-month doesn't lower it. Counted: every logged minute, overtime or not, plus paid
    /// excuses (unpaid ones don't count). From <see cref="WorkWeek.BreakCountsFrom"/> the expected hours include the
    /// break and so does what is counted: the break taken each day (none on a full 9 to 5 logged, which worked it). Paid: what is counted past the threshold, taken from the month's latest
    /// overtime, and never more than the overtime left after making up a short month.
    /// </summary>
    /// <param name="Minutes">The threshold, in minutes.</param>
    public sealed record Threshold(int Minutes, int Counted)
    {
        public int Past => Math.Max(0, Counted - Minutes);
    }

    /// <summary>A working day they were employed on, and the salary it is paid on.</summary>
    /// <param name="Minutes">The day's paid minutes, break included.</param>
    private sealed record PaidDay(DateOnly Day, int Minutes, PayTerms Terms, decimal HourlyRate);

    /// <returns>Null when the person had no pay yet, or wasn't employed on any day of the month.</returns>
    public static Result? Calculate(Input input)
    {
        var next = input.Month.AddMonths(1);
        var lastDay = LastDayOf(input);
        bool Employed(DateOnly day) => EmployedOn(input, lastDay, day);

        var terms = PaidTermsOf(input);
        if (terms is null)
        {
            return null;
        }

        // a fixed salary doesn't depend on hours: logged time earns no overtime or paid breaks
        IReadOnlyList<Overtime.Split> work = input.FixedSalary ? [] : Overtime.Of(input.Work, input.Calendar);
        var ledger = input.LedgerApplies ? LedgerOf(input, work) : null;
        int MadeUp(Overtime.Split w) => ledger?.MadeUpByWork.GetValueOrDefault(w.Id) ?? 0;
        decimal HourlyOn(DateOnly day) => HourlyRateOn(input, terms, day);

        var monthDays = HoursLedger.Days(input.Month, next)
            .Select(d => (Day: d, Hours: input.Calendar.HoursOn(d)))
            .Where(d => d.Hours is not null)
            .ToList();
        var proRated = input.JoinedOn > input.Month
            || lastDay < next.AddDays(-1)
            || (input.Gaps ?? []).Any(g => g.From < next && g.To >= input.Month);

        var threshold = !input.FixedSalary ? ThresholdOf(input, monthDays, work) : null;
        var paidByWork = threshold is null
            ? work.ToDictionary(w => w.Id, w => w.Overtime - MadeUp(w))
            : PastThreshold(work, MadeUp, threshold.Past);

        var monthMinutes = monthDays.Sum(d => d.Hours!.Span);
        var days = monthDays
            .Where(d => Employed(d.Day))
            .Select(d => (d.Day, Minutes: d.Hours!.Span, Terms: TermsFor(input, terms, d.Day)))
            .Select(d => new PaidDay(d.Day, d.Minutes, d.Terms, d.Terms.HourlyRateOn(d.Day, input.Calendar)))
            .ToList();
        var parts = PartsOf(days, monthMinutes);
        var basePay = monthMinutes > 0 ? parts.Sum(p => p.Pay) : terms.Amount;
        var employedMinutes = days.Sum(d => d.Minutes);
        var rate = employedMinutes > 0 ? Math.Round(days.Sum(d => d.HourlyRate * d.Minutes) / employedMinutes, 4, MidpointRounding.AwayFromZero) : 0;

        var overtimePay = work.Sum(w => paidByWork.GetValueOrDefault(w.Id) / 60m
            * TermsFor(input, terms, w.Day).OvertimeRate * input.Calendar.PolicyOn(w.Day).OvertimeMultiplier);
        var overtimeAtHourlyRate = work.Sum(w => paidByWork.GetValueOrDefault(w.Id) / 60m * HourlyOn(w.Day));

        // the shortfall and excused minutes have no day: the blended rate; an unpaid day off costs its own day
        var undated = (ledger is { Flagged: true } ? ShortfallMinutes(ledger, input.Calendar) : 0) + ExcusedMinutes(input, paid: false);
        var unpaidDays = ExcusedDays(input, next, paid: false).ToList();
        var deduction = input.Excuses.Any(e => e.Kind == ExcuseKind.WholeMonth && !e.IsPaid)
            ? basePay
            : Math.Min(basePay, undated / 60m * rate + unpaidDays.Sum(d => d.Minutes / 60m * HourlyOn(d.Day)));
        basePay -= deduction;

        return new Result(
            terms,
            rate,
            terms.OvertimeRate,
            ledger,
            work,
            work.Sum(w => w.Normal),
            work.Sum(w => w.Overtime),
            ledger?.MadeUp ?? 0,
            paidByWork.Values.Sum(),
            ledger?.Excused ?? 0,
            proRated,
            monthDays.Count,
            days.Count,
            undated + unpaidDays.Sum(d => d.Minutes),
            Round(deduction),
            Round(basePay),
            Round(overtimePay),
            threshold?.Minutes,
            threshold?.Counted ?? 0,
            paidByWork,
            TimeOffDaysOf(input, next, Employed),
            parts.Select(p => p with { Pay = Round(p.Pay) }).ToList(),
            overtimeAtHourlyRate);
    }

    /// <summary>The month's days grouped into stretches on one salary, each with what it earned (unrounded).</summary>
    private static List<Part> PartsOf(IReadOnlyList<PaidDay> days, int monthMinutes)
    {
        var parts = new List<Part>();
        var start = 0;
        for (var i = 1; i <= days.Count; i++)
        {
            if (i < days.Count && days[i].Terms == days[start].Terms)
            {
                continue;
            }

            var run = days.Skip(start).Take(i - start).ToList();
            var terms = run[0].Terms;
            var pay = monthMinutes > 0 ? terms.Amount * run.Sum(d => d.Minutes) / monthMinutes : 0;
            parts.Add(new Part(run[0].Day, run[^1].Day, terms.Amount, terms.OvertimeRate, run.Count, pay));
            start = i;
        }

        return parts;
    }

    /// <summary>
    /// The pay a day is paid on: the one in force that day, or their earliest for a day before it; the month's own when
    /// that is in another currency.
    /// </summary>
    private static PayTerms TermsFor(Input input, PayTerms month, DateOnly day)
    {
        var terms = TermsOn(input.PayHistory, day) ?? input.PayHistory.MinBy(t => t.EffectiveFrom) ?? month;
        return string.Equals(terms.Currency, month.Currency, StringComparison.OrdinalIgnoreCase) ? terms : month;
    }

    /// <summary>What an hour of the day's salary is worth that month.</summary>
    private static decimal HourlyRateOn(Input input, PayTerms month, DateOnly day)
        => TermsFor(input, month, day).HourlyRateOn(day, input.Calendar);

    private static int TimeOffDaysOf(Input input, DateOnly next, Func<DateOnly, bool> employed)
        => input.Excuses.Where(e => e.TimeOff)
            .SelectMany(e => e.Days)
            .Distinct()
            .Count(d => d >= input.Month && d < next && employed(d) && input.Calendar.HoursOn(d) is not null);

    /// <summary>
    /// Whether the month's overtime is paid past a threshold: <see cref="Calculate"/>'s <see cref="Result.ThresholdMinutes"/>
    /// is set, without working out the pay.
    /// </summary>
    public static bool HasThreshold(Input input)
        => !input.FixedSalary
            && input.Calendar.PolicyOn(input.Month).OvertimeThresholdHours > 0
            && PaidTermsOf(input) is not null;

    /// <summary>The terms the month is paid on; null when the person had no pay yet, or wasn't employed on any day of it.</summary>
    private static PayTerms? PaidTermsOf(Input input)
    {
        var lastDay = LastDayOf(input);
        var terms = TermsOn(input.PayHistory, input.Today < lastDay ? input.Today : lastDay);
        return terms is not null && HoursLedger.Days(input.Month, input.Month.AddMonths(1)).Any(d => EmployedOn(input, lastDay, d)) ? terms : null;
    }

    /// <summary>The month's last day, or the day they left if that's earlier.</summary>
    private static DateOnly LastDayOf(Input input)
    {
        var end = input.Month.AddMonths(1).AddDays(-1);
        return input.LeftOn is { } left && left < end ? left : end;
    }

    private static bool EmployedOn(Input input, DateOnly lastDay, DateOnly day)
        => day >= input.JoinedOn && day <= lastDay && !Employment.InGap(input.Gaps, day);

    /// <summary>The month's threshold, from the working time in force on its first day; null where it is 0 (off).</summary>
    /// <param name="monthDays">Every working day of the month on the person's calendar, employed or not.</param>
    public static Threshold? ThresholdOf(Input input, IReadOnlyList<(DateOnly Day, WorkHours? Hours)> monthDays, IReadOnlyList<Overtime.Split> work)
    {
        var hours = input.Calendar.PolicyOn(input.Month).OvertimeThresholdHours;
        if (hours <= 0)
        {
            return null;
        }

        var expected = monthDays.Sum(d => d.Hours!.Expected);
        var paidExcuses = input.Excuses.Where(e => e.IsPaid).Sum(e => e.Kind == ExcuseKind.WholeMonth
            ? expected
            : (e.Minutes ?? 0) + HoursLedger.BreakShareOf(e.Minutes ?? 0, monthDays.Select(d => d.Hours!))
                + monthDays.Where(d => e.Days.Contains(d.Day)).Sum(d => d.Hours!.Expected));
        var normalByDay = work.GroupBy(w => w.Day).ToDictionary(g => g.Key, g => g.Sum(w => w.Normal));
        var breaks = monthDays.Where(d => d.Hours!.BreakCounts).Sum(d => d.Hours!.BreakTaken(normalByDay.GetValueOrDefault(d.Day)));
        return new Threshold(Math.Max(hours * 60, expected), work.Sum(w => w.Minutes) + breaks + paidExcuses);
    }

    /// <summary>The overtime paid past the threshold, by work entry: <paramref name="past"/> minutes taken from the latest overtime first.</summary>
    private static Dictionary<long, int> PastThreshold(IReadOnlyList<Overtime.Split> work, Func<Overtime.Split, int> madeUp, int past)
    {
        var paid = new Dictionary<long, int>();
        foreach (var w in work.OrderByDescending(w => w.Day).ThenByDescending(w => w.Id))
        {
            var take = Math.Min(past, w.Overtime - madeUp(w));
            if (take > 0)
            {
                paid[w.Id] = take;
                past -= take;
            }
        }

        return paid;
    }

    /// <summary>The month's hours ledger, from its work split into normal and overtime minutes.</summary>
    public static HoursLedger.Result LedgerOf(Input input, IReadOnlyList<Overtime.Split> work)
        => HoursLedger.Build(input.Month, input.Today, input.JoinedOn, input.Calendar, work, new HoursLedger.Excuses(
            input.Excuses.Any(e => e.Kind == ExcuseKind.WholeMonth),
            input.Excuses.Sum(e => e.Minutes ?? 0),
            input.Excuses.SelectMany(e => e.Days).ToHashSet()),
            input.LeftOn,
            input.Gaps);

    /// <summary>The pay in force on a day: the latest that has started (pay history holds only active entries).</summary>
    public static PayTerms? TermsOn(IEnumerable<PayTerms> history, DateOnly day)
        => history.Where(t => t.EffectiveFrom <= day).OrderBy(t => t.EffectiveFrom).LastOrDefault();

    /// <summary>
    /// The shortfall, plus the paid break of each missed day the shortfall still covers: a whole missed day costs
    /// the full paid day, not just its required hours. Once the break counts, the shortfall already holds it. Deducted once the month closes; while it runs, it is what
    /// would come off.
    /// </summary>
    public static int ShortfallMinutes(HoursLedger.Result ledger, PersonCalendar calendar)
    {
        var remaining = ledger.Short;
        var breaks = 0;
        foreach (var hours in ledger.MissedDays.Select(calendar.HoursOn).OfType<WorkHours>())
        {
            if (remaining < hours.Expected)
            {
                break;
            }

            remaining -= hours.Expected;
            breaks += hours.BreakCounts ? 0 : hours.PaidBreak;
        }

        return ledger.Short + breaks;
    }

    /// <summary>
    /// What a month paid for paid leave. Nothing is logged for it, so no project carries it: it is a company cost.
    /// Paid excused minutes at the month's blended rate, and the full paid day of each paid day off (paid time off
    /// included) at that day's rate, as unpaid ones are deducted; a whole month off with pay is the month's base pay. A
    /// fixed salary is a company cost whole, so it has none.
    /// </summary>
    public static decimal PaidLeavePay(Input input, Result pay)
    {
        if (input.FixedSalary)
        {
            return 0;
        }

        return input.Excuses.Any(e => e.Kind == ExcuseKind.WholeMonth && e.IsPaid)
            ? pay.BasePay
            : Round(ExcusedMinutes(input, paid: true) / 60m * pay.HourlyRate
                + ExcusedDays(input, input.Month.AddMonths(1), paid: true).Sum(d => d.Minutes / 60m * HourlyRateOn(input, pay.Terms, d.Day)));
    }

    /// <summary>
    /// What a salary paid for the part of its breaks no project carries: a day logged only in part shares only that part
    /// of its break with its projects (<see cref="Finance.PaidBreaks"/>), while the whole break is paid.
    /// </summary>
    public static decimal UnsharedBreakPay(Input input, Result pay)
    {
        if (input.FixedSalary)
        {
            return 0;
        }

        var work = input.Work.Select(w => new Finance.PaidBreaks.Work(w.Id, 0, w.Day, w.Start, w.Minutes, w.AllOvertime)).ToList();
        return Round(Finance.PaidBreaks.Unshared(work, input.Calendar)
            .Where(d => d.Day >= input.Month && d.Day < input.Month.AddMonths(1))
            .Sum(d => d.Minutes / 60m * HourlyRateOn(input, pay.Terms, d.Day)));
    }

    /// <summary>
    /// What a salary paid for overtime past its threshold beyond the hourly rate. Projects price every logged hour of a
    /// month with a threshold at its day's hourly rate (<c>EntrySplit.NoPremium</c>), while the overtime past it is paid
    /// at the overtime rate × the multiplier: the rest reaches no project, so it is a company cost. None where no
    /// threshold applies: projects then carry the overtime at its own rate, and a fixed salary has none.
    /// </summary>
    public static decimal OvertimePremium(Result pay)
        => pay.ThresholdMinutes is null ? 0 : Round(pay.OvertimePay - pay.OvertimeAtHourlyRate);

    /// <summary>The month's pay as a monthly amount: the salary in force on its last day (or today).</summary>
    public static decimal MonthlyPay(Result pay) => pay.Terms.Amount;

    /// <summary>The excused minutes that are paid (or unpaid). A whole month is left to the caller.</summary>
    private static int ExcusedMinutes(Input input, bool paid)
        => input.Excuses.Where(e => e.IsPaid == paid && e.Kind != ExcuseKind.WholeMonth).Sum(e => e.Minutes ?? 0);

    /// <summary>Each paid (or unpaid) day off within the month while employed, with its full paid day.</summary>
    private static IEnumerable<(DateOnly Day, int Minutes)> ExcusedDays(Input input, DateOnly next, bool paid)
        => input.Excuses.Where(e => e.IsPaid == paid && e.Kind != ExcuseKind.WholeMonth)
            .SelectMany(e => e.Days)
            .Where(d => d >= input.Month && d < next && d >= input.JoinedOn && !(d > input.LeftOn) && !Employment.InGap(input.Gaps, d))
            .Select(d => (Day: d, Minutes: input.Calendar.HoursOn(d)?.Span ?? 0));

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
