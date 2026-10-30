using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;
using Shouldly;
using Work = ID_Indetails.api.Business.Payroll.Overtime.Work;

namespace ID_Indetails.UnitTests.Business;

/// <summary>
/// Someone whose account was deactivated on Monday 15 June 2026: 11 of June's 22 working days fall on or before it.
/// Their final month is pro-rated to that day, and their hours ledger ends there.
/// </summary>
public class LeavingPayTests
{
    private static readonly DateOnly June = new(2026, 6, 1);
    private static readonly DateOnly LeftOn = new(2026, 6, 15);
    private static readonly int[] WorkingDaysToLeaving = [1, 2, 3, 4, 5, 8, 9, 10, 11, 12, 15];

    // Monday to Friday, 9 to 5, a paid 60-minute break (7h required, 8h paid).
    private static readonly PersonCalendar Calendar = new([new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 1020, 60, 1m)], []);

    // 1,760 a month is 10 an hour in June (176 paid hours).
    private static readonly SalaryCalculator.PayTerms Monthly = new(new(2026, 1, 1), PayBasis.Monthly, 1760m, "USD", 9.57m);

    private static SalaryCalculator.Result? Pay(DateOnly month, DateOnly today, IEnumerable<int> workedDays, DateOnly? leftOn = null)
        => SalaryCalculator.Calculate(new SalaryCalculator.Input(
            month,
            today,
            new DateOnly(2026, 1, 1),
            LedgerApplies: true,
            [Monthly],
            Calendar,
            workedDays.Select(d => new Work(d, new DateOnly(2026, 6, d), 540, 420)).ToList(),
            [],
            leftOn ?? LeftOn));

    [Fact]
    public void The_final_month_is_pro_rated_to_the_leaving_day_and_the_ledger_ends_there()
    {
        var pay = Pay(June, today: new DateOnly(2026, 7, 10), WorkingDaysToLeaving)!;

        pay.ShouldSatisfyAllConditions(
            p => p.ProRated.ShouldBeTrue(),
            p => p.WorkingDays.ShouldBe(22),
            p => p.WorkingDaysPaid.ShouldBe(11),
            p => p.BasePay.ShouldBe(880m),
            p => p.Deduction.ShouldBe(0m));
        pay.Ledger!.ShouldSatisfyAllConditions(
            l => l.Expected.ShouldBe(11 * 420),
            l => l.Short.ShouldBe(0),
            l => l.MissedDays.ShouldBeEmpty());
    }

    [Fact]
    public void A_missed_day_before_leaving_still_costs_the_full_paid_day()
    {
        var pay = Pay(June, today: new DateOnly(2026, 7, 10), WorkingDaysToLeaving.Where(d => d != 15))!;

        pay.Ledger!.MissedDays.ShouldBe([LeftOn]);
        pay.DeductionMinutes.ShouldBe(480);
        pay.BasePay.ShouldBe(880m - 80m);
    }

    [Fact]
    public void Once_they_have_left_their_month_is_closed_even_while_the_calendar_month_runs()
    {
        var pay = Pay(June, today: new DateOnly(2026, 6, 20), WorkingDaysToLeaving.Where(d => d != 15))!;

        pay.Ledger!.ShouldSatisfyAllConditions(
            l => l.InProgress.ShouldBeFalse(),
            l => l.Flagged.ShouldBeTrue(),
            l => l.Expected.ShouldBe(11 * 420));
        pay.Deduction.ShouldBe(80m);
    }

    [Fact]
    public void Nothing_is_owed_for_a_month_after_they_left()
        => Pay(new DateOnly(2026, 7, 1), today: new DateOnly(2026, 7, 10), []).ShouldBeNull();
}
