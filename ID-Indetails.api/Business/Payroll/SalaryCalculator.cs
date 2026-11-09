using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// What someone is owed for a month. Terms are the pay in force at the end of the month (or today,
/// while it is in progress). An hour of monthly pay is the salary ÷ the month's paid hours (<see cref="PayRules.HourlyRate"/>),
/// which keeps the salary fixed; overtime is paid at the pay's own overtime rate instead (the salary ÷ 184 unless set
/// otherwise, <see cref="PayRules.SuggestedOvertimeRate"/>), whatever the month.
/// <list type="bullet">
/// <item><b>Monthly:</b> the salary, pro-rated on the paid hours of the days they were employed if they joined, left or came back
/// mid-month (days away between two periods of employment aren't paid), plus overtime at the multiplier. In a closed month the hours ledger's shortfall is deducted at the
/// month's hourly rate, and each missed working day also loses its paid break, so it costs the full paid day.
/// Unpaid excuses are deducted the same way (an unpaid day off costs the full paid day).
/// From 1 November 2026 (the working time's overtime threshold, 184 hours) a salary's overtime is paid only for the hours
/// counted past the month's threshold, see <see cref="Threshold"/>.</item>
/// <item><b>Hourly:</b> logged hours at the rate in force that day, overtime that made up a shortfall at the
/// plain rate, the rest at the multiplier, plus the paid break of each working day they worked.</item>
/// </list>
/// </summary>
public static class SalaryCalculator
{
    /// <param name="OvertimeRate">What an hour of overtime is paid at, before the multiplier: stored with the pay.</param>
    public sealed record PayTerms(DateOnly EffectiveFrom, PayBasis Basis, decimal Amount, string Currency, decimal OvertimeRate)
    {
        public decimal HourlyRateOn(DateOnly day, PersonCalendar calendar) => PayRules.HourlyRate(Basis, Amount, day, calendar);
    }

    public sealed record Excuse(ExcuseKind Kind, int? Minutes, IReadOnlyList<DateOnly> Days, bool IsPaid);

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

    /// <param name="HourlyRate">What an hour of <paramref name="Terms"/> is worth this month: it prices deductions.</param>
    /// <param name="OvertimeRate">The overtime rate of <paramref name="Terms"/>: overtime is paid at it × the multiplier.</param>
    /// <param name="OvertimePaidMinutes">The overtime paid as overtime: past the threshold where one applies, otherwise what didn't make up a short month.</param>
    /// <param name="ThresholdMinutes">A salary's overtime is paid only past this many counted minutes; null where no threshold applies.</param>
    /// <param name="CountedMinutes">What counts toward the threshold: every logged minute plus paid excuses; 0 where none applies.</param>
    /// <param name="PaidOvertimeByWork">The overtime paid as overtime, by work entry.</param>
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
        int PaidBreakDays,
        decimal PaidBreakPay,
        int DeductionMinutes,
        decimal Deduction,
        decimal BasePay,
        decimal OvertimePay,
        int? ThresholdMinutes = null,
        int CountedMinutes = 0,
        IReadOnlyDictionary<long, int>? PaidOvertimeByWork = null)
    {
        public decimal Total => BasePay + OvertimePay;
    }

    /// <summary>
    /// When a salary's overtime is paid past a threshold: the working time's hours (184 from 1 November 2026) or the
    /// month's expected hours if those are more, so overtime is only paid once the month's own hours are done. It is
    /// fixed: joining or leaving mid-month doesn't lower it. Counted: every logged minute, overtime or not, plus paid
    /// excuses (unpaid ones don't count). Paid: what is counted past the threshold, taken from the month's latest
    /// overtime, and never more than the overtime left after making up a short month.
    /// </summary>
    /// <param name="Minutes">The threshold, in minutes.</param>
    public sealed record Threshold(int Minutes, int Counted)
    {
        public int Past => Math.Max(0, Counted - Minutes);
    }

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

        var rate = terms.HourlyRateOn(input.Month, input.Calendar);
        // a fixed salary doesn't depend on hours: logged time earns no overtime or paid breaks
        IReadOnlyList<Overtime.Split> work = input.FixedSalary ? [] : Overtime.Of(input.Work, input.Calendar);
        var ledger = input.LedgerApplies ? LedgerOf(input, work) : null;
        int MadeUp(Overtime.Split w) => ledger?.MadeUpByWork.GetValueOrDefault(w.Id) ?? 0;
        decimal OvertimePay(Overtime.Split w, PayTerms t, int minutes)
            => minutes / 60m * t.OvertimeRate * input.Calendar.PolicyOn(w.Day).OvertimeMultiplier;

        var monthDays = HoursLedger.Days(input.Month, next)
            .Select(d => (Day: d, Hours: input.Calendar.HoursOn(d)))
            .Where(d => d.Hours is not null)
            .ToList();
        var paidDays = monthDays.Count(d => Employed(d.Day));
        var proRated = input.JoinedOn > input.Month
            || lastDay < next.AddDays(-1)
            || (input.Gaps ?? []).Any(g => g.From < next && g.To >= input.Month);

        decimal basePay, overtimePay, paidBreakPay = 0, deduction = 0;
        int paidBreakDays = 0, deductionMinutes = 0;
        var threshold = terms.Basis == PayBasis.Monthly && !input.FixedSalary ? ThresholdOf(input, monthDays, work) : null;
        var paidByWork = threshold is null
            ? work.ToDictionary(w => w.Id, w => w.Overtime - MadeUp(w))
            : PastThreshold(work, MadeUp, threshold.Past);

        if (terms.Basis == PayBasis.Monthly)
        {
            var monthMinutes = monthDays.Sum(d => d.Hours!.Span);
            var employedMinutes = monthDays.Where(d => Employed(d.Day)).Sum(d => d.Hours!.Span);
            basePay = proRated && monthMinutes > 0 ? terms.Amount * employedMinutes / monthMinutes : terms.Amount;
            overtimePay = work.Sum(w => OvertimePay(w, terms, paidByWork.GetValueOrDefault(w.Id)));

            deductionMinutes = (ledger is { Flagged: true } ? ShortfallMinutes(ledger, input.Calendar) : 0) + UnpaidMinutes(input, next);
            deduction = input.Excuses.Any(e => e.Kind == ExcuseKind.WholeMonth && !e.IsPaid)
                ? basePay
                : Math.Min(basePay, deductionMinutes / 60m * rate);
            basePay -= deduction;
        }
        else
        {
            decimal RateOn(DateOnly day) => (TermsOn(input.PayHistory, day) ?? terms).HourlyRateOn(day, input.Calendar);

            var breakDays = work.Where(w => w.Normal > 0)
                .Select(w => w.Day)
                .Distinct()
                .Select(day => (Day: day, Hours: input.Calendar.HoursOn(day)))
                .Where(d => d.Hours is { PaidBreak: > 0 })
                .ToList();
            paidBreakDays = breakDays.Count;
            paidBreakPay = breakDays.Sum(d => d.Hours!.PaidBreak / 60m * RateOn(d.Day));

            basePay = work.Sum(w => (w.Normal + MadeUp(w)) / 60m * RateOn(w.Day)) + paidBreakPay;
            overtimePay = work.Sum(w => OvertimePay(w, TermsOn(input.PayHistory, w.Day) ?? terms, paidByWork.GetValueOrDefault(w.Id)));
        }

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
            paidDays,
            paidBreakDays,
            Round(paidBreakPay),
            deductionMinutes,
            Round(deduction),
            Round(basePay),
            Round(overtimePay),
            threshold?.Minutes,
            threshold?.Counted ?? 0,
            paidByWork);
    }

    /// <summary>
    /// Whether the month's overtime is paid past a threshold: <see cref="Calculate"/>'s <see cref="Result.ThresholdMinutes"/>
    /// is set, without working out the pay.
    /// </summary>
    public static bool HasThreshold(Input input)
        => !input.FixedSalary
            && input.Calendar.PolicyOn(input.Month).OvertimeThresholdHours > 0
            && PaidTermsOf(input) is { Basis: PayBasis.Monthly };

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

        var expected = monthDays.Sum(d => d.Hours!.Required);
        var paidExcuses = input.Excuses.Where(e => e.IsPaid).Sum(e => e.Kind == ExcuseKind.WholeMonth
            ? expected
            : (e.Minutes ?? 0) + monthDays.Where(d => e.Days.Contains(d.Day)).Sum(d => d.Hours!.Required));
        return new Threshold(Math.Max(hours * 60, expected), work.Sum(w => w.Minutes) + paidExcuses);
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

    public static PayTerms? TermsOn(IEnumerable<PayTerms> history, DateOnly day)
        => history.Where(t => t.EffectiveFrom <= day).OrderBy(t => t.EffectiveFrom).LastOrDefault();

    /// <summary>
    /// The shortfall, plus the paid break of each missed day the shortfall still covers: a whole missed day costs
    /// the full paid day, not just its required hours. Deducted once the month closes; while it runs, it is what
    /// would come off.
    /// </summary>
    public static int ShortfallMinutes(HoursLedger.Result ledger, PersonCalendar calendar)
    {
        var remaining = ledger.Short;
        var breaks = 0;
        foreach (var hours in ledger.MissedDays.Select(calendar.HoursOn).OfType<WorkHours>())
        {
            if (remaining < hours.Required)
            {
                break;
            }

            remaining -= hours.Required;
            breaks += hours.PaidBreak;
        }

        return ledger.Short + breaks;
    }

    /// <summary>
    /// What a monthly salary pays for paid leave (excused time off that is paid): the paid excused minutes, and the full
    /// paid day of each paid day off, at the month's hourly rate, as unpaid ones are deducted; a whole month off with pay
    /// is the month's base pay. Nothing is logged for it, so no project carries it: it is a company cost. Hourly pay is
    /// paid for hours worked only, and a fixed salary is a company cost whole, so neither has any.
    /// </summary>
    public static decimal PaidLeavePay(Input input, Result pay)
    {
        if (input.FixedSalary || pay.Terms.Basis != PayBasis.Monthly)
        {
            return 0;
        }

        return input.Excuses.Any(e => e.Kind == ExcuseKind.WholeMonth && e.IsPaid)
            ? pay.BasePay
            : Round(ExcusedMinutes(input, input.Month.AddMonths(1), paid: true) / 60m * pay.HourlyRate);
    }

    /// <summary>Unpaid excused minutes, and the full paid day of each unpaid day off within the month.</summary>
    private static int UnpaidMinutes(Input input, DateOnly next) => ExcusedMinutes(input, next, paid: false);

    /// <summary>
    /// The excused minutes that are paid (or unpaid), and the full paid day of each such day off within the month while
    /// employed. A whole month is left to the caller.
    /// </summary>
    private static int ExcusedMinutes(Input input, DateOnly next, bool paid)
        => input.Excuses.Where(e => e.IsPaid == paid && e.Kind != ExcuseKind.WholeMonth).Sum(e =>
            (e.Minutes ?? 0)
            + e.Days.Where(d => d >= input.Month && d < next && d >= input.JoinedOn && !(d > input.LeftOn) && !Employment.InGap(input.Gaps, d))
                .Sum(d => input.Calendar.HoursOn(d)?.Span ?? 0));

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
