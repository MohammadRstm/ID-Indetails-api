using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;
using Shouldly;
using Work = ID_Indetails.api.Business.Payroll.Overtime.Work;

namespace ID_Indetails.UnitTests.Business;

/// <summary>
/// The company week drops its break from Tuesday 1 December 2026, so the whole 8-hour day is logged and required.
/// November keeps its paid 60-minute break (7h required, 8h paid). December 2026 has 23 Monday–Friday working days.
/// </summary>
public class LoggedBreakTests
{
    private static readonly DateOnly November = new(2026, 11, 1);
    private static readonly DateOnly December = new(2026, 12, 1);
    private static readonly DateOnly Monday30November = new(2026, 11, 30);
    private static readonly DateOnly Today = new(2027, 1, 10);

    private static readonly PersonCalendar Calendar = new(
        [
            new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 1020, 60, 1.5m),
            new PersonCalendar.Policy(December, 62, 540, 1020, 0, 1.5m),
        ],
        []);

    // 1,840 a month is 10 an hour in December (23 days of 8 paid hours).
    private static readonly SalaryCalculator.PayTerms Monthly = new(new(2026, 1, 1), PayBasis.Monthly, 1840m, "USD");
    private static readonly SalaryCalculator.PayTerms Hourly = new(new(2026, 1, 1), PayBasis.Hourly, 20m, "USD");

    private static IEnumerable<DateOnly> WorkingDays(DateOnly month)
        => HoursLedger.Days(month, month.AddMonths(1)).Where(d => Calendar.HoursOn(d) is not null);

    private static SalaryCalculator.Result Pay(SalaryCalculator.PayTerms terms, DateOnly month, IReadOnlyList<Work> work, bool ledger = true)
        => SalaryCalculator.Calculate(new SalaryCalculator.Input(month, Today, new(2026, 1, 1), ledger, [terms], Calendar, work, []))!;

    [Fact]
    public void Without_a_break_the_whole_day_is_required_and_none_of_it_is_a_paid_break()
    {
        WorkWeek.RequiredMinutes(540, 1020, 0).ShouldBe(480);

        Calendar.HoursOn(Monday30November)!.Required.ShouldBe(420);
        Calendar.HoursOn(Monday30November)!.PaidBreak.ShouldBe(60);
        Calendar.HoursOn(December)!.Required.ShouldBe(480);
        Calendar.HoursOn(December)!.PaidBreak.ShouldBe(0);
    }

    [Fact]
    public void Working_straight_through_the_day_is_no_longer_overtime()
    {
        var splits = Overtime.Of([new Work(1, Monday30November, 540, 480), new Work(2, December, 540, 480)], Calendar);

        splits.Single(s => s.Id == 1).Overtime.ShouldBe(60);
        splits.Single(s => s.Id == 2).Overtime.ShouldBe(0);
    }

    [Fact]
    public void Expected_hours_count_the_whole_day()
    {
        MyTime.ExpectedFullMonth(November, new(2026, 1, 1), Calendar).ShouldBe(21 * 420);
        MyTime.ExpectedFullMonth(December, new(2026, 1, 1), Calendar).ShouldBe(23 * 480);
    }

    [Fact]
    public void Logging_7_hours_a_day_leaves_a_salaried_month_an_hour_a_day_short()
    {
        var result = Pay(Monthly, December, WorkingDays(December).Select((d, i) => new Work(i + 1, d, 540, 420)).ToList());

        result.Ledger!.Expected.ShouldBe(23 * 480);
        result.Ledger.Short.ShouldBe(23 * 60);
        result.Ledger.Flagged.ShouldBeTrue();
        result.HourlyRate.ShouldBe(10m);
        result.Deduction.ShouldBe(230m);
        result.Total.ShouldBe(1610m);
    }

    [Fact]
    public void Logging_the_whole_day_earns_the_full_salary_with_no_overtime()
    {
        var result = Pay(Monthly, December, WorkingDays(December).Select((d, i) => new Work(i + 1, d, 540, 480)).ToList());

        result.Ledger!.Short.ShouldBe(0);
        result.OvertimeMinutes.ShouldBe(0);
        result.Total.ShouldBe(1840m);
    }

    [Fact]
    public void A_missed_day_still_costs_the_full_8_hour_day()
    {
        var work = WorkingDays(December).Where(d => d.Day != 31).Select((d, i) => new Work(i + 1, d, 540, 480)).ToList();

        var result = Pay(Monthly, December, work);

        result.Ledger!.MissedDays.ShouldBe([new DateOnly(2026, 12, 31)]);
        SalaryCalculator.ShortfallMinutes(result.Ledger, Calendar).ShouldBe(480);
        result.Deduction.ShouldBe(80m);
    }

    [Fact]
    public void Hourly_staff_are_paid_the_hours_they_log_with_no_paid_break_on_top()
    {
        // Without the hours ledger, so overtime isn't spent making up a short month. November: 7h at 20, the paid break
        // at 20 and the 8th logged hour as overtime at 1.5x.
        var november = Pay(Hourly, November, [new Work(1, Monday30November, 540, 480)], ledger: false);
        november.PaidBreakDays.ShouldBe(1);
        november.BasePay.ShouldBe(160m);
        november.OvertimePay.ShouldBe(30m);

        var december = Pay(Hourly, December, [new Work(1, December, 540, 480)], ledger: false);
        december.PaidBreakDays.ShouldBe(0);
        december.PaidBreakPay.ShouldBe(0m);
        december.BasePay.ShouldBe(160m);
        december.OvertimePay.ShouldBe(0m);
    }

    [Fact]
    public void Projects_carry_no_break_cost_once_the_break_is_logged()
    {
        var shares = PaidBreaks.Allocate(
            [new PaidBreaks.Work(1, 10, Monday30November, 540, 480), new PaidBreaks.Work(2, 10, December, 540, 480)],
            Calendar);

        shares.ShouldBe([new PaidBreaks.Share(10, Monday30November, 60m)]);
    }

    [Fact]
    public void The_reminder_waits_for_the_whole_day()
    {
        var hours = Calendar.HoursOn(December);

        ReminderRules.LogReminderDue(hours, 16 * 60 + 30, 420, false).ShouldBeTrue();
        ReminderRules.LogReminderDue(hours, 16 * 60 + 30, 480, false).ShouldBeFalse();
    }
}
