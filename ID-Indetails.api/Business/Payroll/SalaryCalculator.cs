using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// What someone is owed for a month. Terms are the pay in force at the end of the month (or today,
/// while it is in progress). An hour of monthly pay is the salary ÷ the month's paid hours (<see cref="PayRules.HourlyRate"/>),
/// which keeps the salary fixed; overtime is paid at the true hourly rate of a standard month instead
/// (<see cref="PayRules.StandardHourlyRate"/>), whatever the month.
/// <list type="bullet">
/// <item><b>Monthly:</b> the salary, pro-rated on the paid hours of the days they were employed if they joined, left or came back
/// mid-month (days away between two periods of employment aren't paid), plus overtime at the multiplier. In a closed month the hours ledger's shortfall is deducted at the
/// month's hourly rate, and each missed working day also loses its paid break, so it costs the full paid day.
/// Unpaid excuses are deducted the same way (an unpaid day off costs the full paid day).</item>
/// <item><b>Hourly:</b> logged hours at the rate in force that day, overtime that made up a shortfall at the
/// plain rate, the rest at the multiplier, plus the paid break of each working day they worked.</item>
/// </list>
/// </summary>
public static class SalaryCalculator
{
    public sealed record PayTerms(DateOnly EffectiveFrom, PayBasis Basis, decimal Amount, string Currency)
    {
        public decimal HourlyRateOn(DateOnly day, PersonCalendar calendar) => PayRules.HourlyRate(Basis, Amount, day, calendar);

        public decimal StandardHourlyRateOn(DateOnly day, PersonCalendar calendar) => PayRules.StandardHourlyRate(Basis, Amount, day, calendar);
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
    /// <param name="StandardHourlyRate">The true hourly rate of <paramref name="Terms"/>: overtime is paid at it × the multiplier.</param>
    public sealed record Result(
        PayTerms Terms,
        decimal HourlyRate,
        decimal StandardHourlyRate,
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
        decimal OvertimePay)
    {
        public decimal Total => BasePay + OvertimePay;
    }

    /// <returns>Null when the person had no pay yet, or wasn't employed on any day of the month.</returns>
    public static Result? Calculate(Input input)
    {
        var next = input.Month.AddMonths(1);
        var lastDay = input.LeftOn is { } left && left < next ? left : next.AddDays(-1);
        bool Employed(DateOnly day) => day >= input.JoinedOn && day <= lastDay && !Employment.InGap(input.Gaps, day);

        var termsDay = input.Today < lastDay ? input.Today : lastDay;
        var terms = TermsOn(input.PayHistory, termsDay);
        if (terms is null || !HoursLedger.Days(input.Month, next).Any(Employed))
        {
            return null;
        }

        var rate = terms.HourlyRateOn(input.Month, input.Calendar);
        // a fixed salary doesn't depend on hours: logged time earns no overtime or paid breaks
        IReadOnlyList<Overtime.Split> work = input.FixedSalary ? [] : Overtime.Of(input.Work, input.Calendar);
        var ledger = input.LedgerApplies ? LedgerOf(input, work) : null;
        int MadeUp(Overtime.Split w) => ledger?.MadeUpByWork.GetValueOrDefault(w.Id) ?? 0;
        decimal OvertimePay(Overtime.Split w, PayTerms t)
            => (w.Overtime - MadeUp(w)) / 60m * t.StandardHourlyRateOn(w.Day, input.Calendar) * input.Calendar.PolicyOn(w.Day).OvertimeMultiplier;

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

        if (terms.Basis == PayBasis.Monthly)
        {
            var monthMinutes = monthDays.Sum(d => d.Hours!.Span);
            var employedMinutes = monthDays.Where(d => Employed(d.Day)).Sum(d => d.Hours!.Span);
            basePay = proRated && monthMinutes > 0 ? terms.Amount * employedMinutes / monthMinutes : terms.Amount;
            overtimePay = work.Sum(w => OvertimePay(w, terms));

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
            overtimePay = work.Sum(w => OvertimePay(w, TermsOn(input.PayHistory, w.Day) ?? terms));
        }

        return new Result(
            terms,
            rate,
            terms.StandardHourlyRateOn(termsDay, input.Calendar),
            ledger,
            work,
            work.Sum(w => w.Normal),
            work.Sum(w => w.Overtime),
            ledger?.MadeUp ?? 0,
            work.Sum(w => w.Overtime) - (ledger?.MadeUp ?? 0),
            ledger?.Excused ?? 0,
            proRated,
            monthDays.Count,
            paidDays,
            paidBreakDays,
            Round(paidBreakPay),
            deductionMinutes,
            Round(deduction),
            Round(basePay),
            Round(overtimePay));
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

    /// <summary>Unpaid excused minutes, and the full paid day of each unpaid day off within the month.</summary>
    private static int UnpaidMinutes(Input input, DateOnly next)
        => input.Excuses.Where(e => !e.IsPaid && e.Kind != ExcuseKind.WholeMonth).Sum(e =>
            (e.Minutes ?? 0)
            + e.Days.Where(d => d >= input.Month && d < next && d >= input.JoinedOn && !(d > input.LeftOn) && !Employment.InGap(input.Gaps, d))
                .Sum(d => input.Calendar.HoursOn(d)?.Span ?? 0));

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
