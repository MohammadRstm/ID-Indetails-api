using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// What someone is owed for a month. Terms are the pay in force at the end of the month (or today,
/// while it is in progress); an hour of monthly pay is salary ÷ 173.33.
/// <list type="bullet">
/// <item><b>Monthly:</b> the salary, pro-rated on the person's own working days if they joined mid-month,
/// plus overtime at the multiplier. In a closed month the hours ledger's shortfall is deducted at the
/// hourly rate, and each missed working day also loses its paid break, so it costs the full paid day.
/// Unpaid excuses are deducted the same way (an unpaid day off costs the full paid day).</item>
/// <item><b>Hourly:</b> logged hours at the rate in force that day, overtime that made up a shortfall at the
/// plain rate, the rest at the multiplier, plus the paid break of each working day they worked.</item>
/// </list>
/// </summary>
public static class SalaryCalculator
{
    public sealed record PayTerms(DateOnly EffectiveFrom, PayBasis Basis, decimal Amount, string Currency, decimal HourlyRate);

    public sealed record Excuse(ExcuseKind Kind, int? Minutes, IReadOnlyList<DateOnly> Days, bool IsPaid);

    public sealed record Input(
        DateOnly Month,
        DateOnly Today,
        DateOnly JoinedOn,
        bool LedgerApplies,
        IReadOnlyList<PayTerms> PayHistory,
        PersonCalendar Calendar,
        IReadOnlyList<Overtime.Work> Work,
        IReadOnlyList<Excuse> Excuses);

    public sealed record Result(
        PayTerms Terms,
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

    /// <returns>Null when the person had no pay yet, or joined after the month.</returns>
    public static Result? Calculate(Input input)
    {
        var next = input.Month.AddMonths(1);
        var termsDay = input.Today < next ? input.Today : next.AddDays(-1);
        var terms = TermsOn(input.PayHistory, termsDay);
        if (terms is null || input.JoinedOn >= next)
        {
            return null;
        }

        var work = Overtime.Of(input.Work, input.Calendar);
        var ledger = input.LedgerApplies ? LedgerOf(input, work) : null;
        int MadeUp(Overtime.Split w) => ledger?.MadeUpByWork.GetValueOrDefault(w.Id) ?? 0;

        var monthDays = HoursLedger.Days(input.Month, next).Where(d => input.Calendar.HoursOn(d) is not null).ToList();
        var paidDays = monthDays.Count(d => d >= input.JoinedOn);
        var proRated = input.JoinedOn > input.Month;

        decimal basePay, overtimePay, paidBreakPay = 0, deduction = 0;
        int paidBreakDays = 0, deductionMinutes = 0;

        if (terms.Basis == PayBasis.Monthly)
        {
            basePay = proRated && monthDays.Count > 0 ? terms.Amount * paidDays / monthDays.Count : terms.Amount;
            overtimePay = work.Sum(w => (w.Overtime - MadeUp(w)) / 60m * terms.HourlyRate * input.Calendar.PolicyOn(w.Day).OvertimeMultiplier);

            deductionMinutes = (ledger is { Flagged: true } ? ShortfallMinutes(ledger, input.Calendar) : 0) + UnpaidMinutes(input, next);
            deduction = input.Excuses.Any(e => e.Kind == ExcuseKind.WholeMonth && !e.IsPaid)
                ? basePay
                : Math.Min(basePay, deductionMinutes / 60m * terms.HourlyRate);
            basePay -= deduction;
        }
        else
        {
            decimal RateOn(DateOnly day) => TermsOn(input.PayHistory, day)?.HourlyRate ?? terms.HourlyRate;

            var breakDays = work.Where(w => w.Normal > 0)
                .Select(w => w.Day)
                .Distinct()
                .Select(day => (Day: day, Hours: input.Calendar.HoursOn(day)))
                .Where(d => d.Hours is { PaidBreak: > 0 })
                .ToList();
            paidBreakDays = breakDays.Count;
            paidBreakPay = breakDays.Sum(d => d.Hours!.PaidBreak / 60m * RateOn(d.Day));

            basePay = work.Sum(w => (w.Normal + MadeUp(w)) / 60m * RateOn(w.Day)) + paidBreakPay;
            overtimePay = work.Sum(w => (w.Overtime - MadeUp(w)) / 60m * RateOn(w.Day) * input.Calendar.PolicyOn(w.Day).OvertimeMultiplier);
        }

        return new Result(
            terms,
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
            input.Excuses.SelectMany(e => e.Days).ToHashSet()));

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
            + e.Days.Where(d => d >= input.Month && d < next && d >= input.JoinedOn)
                .Sum(d => input.Calendar.HoursOn(d)?.Span ?? 0));

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
