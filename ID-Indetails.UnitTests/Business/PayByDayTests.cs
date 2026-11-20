using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;
using Shouldly;
using Work = ID_Indetails.api.Business.Payroll.Overtime.Work;

namespace ID_Indetails.UnitTests.Business;

/// <summary>
/// Pay is per day: each working day earns its own salary × its paid minutes ÷ the month's. June 2026 has 22 working days
/// of 8 paid hours (176 hours): 10 before Monday the 15th and 12 from it.
/// </summary>
public class PayByDayTests
{
    private static readonly DateOnly June = new(2026, 6, 1);
    private static readonly DateOnly July10 = new(2026, 7, 10);

    // Monday to Friday, 9 to 5, a paid 60-minute break (7h required, 8h paid), overtime at 1.5x.
    private static readonly PersonCalendar Calendar = new([new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 1020, 60, 1.5m)], []);

    // 1,760 a month is 10 an hour in June; the raise to 2,112 from the 15th is 12 an hour.
    private static readonly SalaryCalculator.PayTerms Salary = new(new(2026, 1, 1), 1760m, "USD", 9.57m);
    private static readonly SalaryCalculator.PayTerms Raise = new(new(2026, 6, 15), 2112m, "USD", 11.48m);

    private static SalaryCalculator.Input June2026(
        IReadOnlyList<SalaryCalculator.PayTerms> pay,
        IReadOnlyList<Work>? work = null,
        IReadOnlyList<SalaryCalculator.Excuse>? excuses = null)
        => new(June, July10, new(2026, 1, 1), false, pay, Calendar, work ?? [], excuses ?? []);

    [Fact]
    public void A_month_on_one_salary_pays_exactly_that_salary()
    {
        var pay = SalaryCalculator.Calculate(June2026([Salary]))!;

        pay.BasePay.ShouldBe(1760m);
        pay.HourlyRate.ShouldBe(10m);
        pay.Parts!.ShouldBe([new SalaryCalculator.Part(June, new(2026, 6, 30), 1760m, 9.57m, 22, 1760m)]);
    }

    [Fact]
    public void A_mid_month_raise_splits_the_month_at_its_day()
    {
        var pay = SalaryCalculator.Calculate(June2026([Salary, Raise]))!;

        // 10 days of 1,760's share (800) and 12 of 2,112's (1,152)
        pay.BasePay.ShouldBe(1952m);
        pay.Parts!.ShouldBe([
            new SalaryCalculator.Part(June, new(2026, 6, 12), 1760m, 9.57m, 10, 800m),
            new SalaryCalculator.Part(new(2026, 6, 15), new(2026, 6, 30), 2112m, 11.48m, 12, 1152m),
        ]);
        // the month's blended rate: 80 hours at 10 and 96 at 12, over 176
        pay.HourlyRate.ShouldBe(11.0909m);
        // the salary shown for the month is the one in force on its last day
        pay.Terms.ShouldBe(Raise);
        SalaryCalculator.MonthlyPay(pay).ShouldBe(2112m);
    }

    [Fact]
    public void Days_before_the_first_salary_are_paid_on_it()
    {
        var pay = SalaryCalculator.Calculate(June2026([Raise]))!;

        pay.BasePay.ShouldBe(2112m);
        pay.Parts!.ShouldHaveSingleItem().Days.ShouldBe(22);
    }

    [Fact]
    public void A_day_off_costs_its_own_days_salary_and_excused_minutes_the_blended_rate()
    {
        var unpaid = new SalaryCalculator.Excuse(ExcuseKind.Days, null, [new(2026, 6, 2), new(2026, 6, 16)], false);
        var pay = SalaryCalculator.Calculate(June2026([Salary, Raise], excuses: [unpaid]))!;

        // Tuesday the 2nd at 10 an hour, Tuesday the 16th at 12: 80 + 96
        pay.DeductionMinutes.ShouldBe(960);
        pay.Deduction.ShouldBe(176m);
        pay.BasePay.ShouldBe(1776m);

        var paid = new SalaryCalculator.Excuse(ExcuseKind.Days, null, [new(2026, 6, 16)], true);
        var hour = new SalaryCalculator.Excuse(ExcuseKind.Minutes, 60, [], true);
        var input = June2026([Salary, Raise], excuses: [paid, hour]);
        // the day at 12 an hour (96) and the hour at the blended 11.0909
        SalaryCalculator.PaidLeavePay(input, SalaryCalculator.Calculate(input)!).ShouldBe(107.09m);
    }

    [Fact]
    public void Overtime_is_paid_at_the_overtime_rate_of_its_days_salary()
    {
        // two hours on Saturday the 6th (before the raise) and two on Saturday the 20th (after it): all overtime
        var work = new[] { new Work(1, new(2026, 6, 6), 600, 120), new Work(2, new(2026, 6, 20), 600, 120) };
        var pay = SalaryCalculator.Calculate(June2026([Salary, Raise], work))!;

        // 2h × 9.57 × 1.5 + 2h × 11.48 × 1.5
        pay.OvertimePay.ShouldBe(63.15m);
        pay.OvertimeAtHourlyRate.ShouldBe(2 * 10m + 2 * 12m);
    }

    [Fact]
    public void A_month_is_paid_in_the_currency_of_its_last_day()
    {
        // an old change of currency in mid-month: every day is paid on the pay in force at the month's end
        var lbp = new SalaryCalculator.PayTerms(new(2026, 6, 15), 157_520_000m, "LBP", 856_087m);
        var pay = SalaryCalculator.Calculate(June2026([Salary, lbp]))!;

        pay.Terms.Currency.ShouldBe("LBP");
        pay.BasePay.ShouldBe(157_520_000m);
        pay.Parts!.ShouldHaveSingleItem().Salary.ShouldBe(157_520_000m);
    }
}
