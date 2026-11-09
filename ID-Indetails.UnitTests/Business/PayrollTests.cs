using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;
using Shouldly;
using Work = ID_Indetails.api.Business.Payroll.Overtime.Work;

namespace ID_Indetails.UnitTests.Business;

/// <summary>June 2026 starts on a Monday and has 22 Monday–Friday working days.</summary>
public class PayrollTests
{
    private static readonly DateOnly June = new(2026, 6, 1);
    private static readonly DateOnly July10 = new(2026, 7, 10);

    // Monday to Friday, 9 to 5, a paid 60-minute break (7h required, 8h paid), overtime at 1.5x.
    private static readonly PersonCalendar.Policy Company = new(new(2000, 1, 1), 62, 540, 1020, 60, 1.5m);

    private static PersonCalendar Calendar(params PersonCalendar.Schedule[] schedules) => new([Company], schedules);

    private static Work At(long id, int day, int startHour, int hours) => new(id, new DateOnly(2026, 6, day), startHour * 60, hours * 60);

    // 1,760 a month is 10 an hour in June (22 days of 8 paid hours), with overtime from 9.57 (1,760 ÷ 184); 20 an hour
    // for the hourly terms, overtime included.
    private static readonly SalaryCalculator.PayTerms Monthly = new(new(2026, 1, 1), PayBasis.Monthly, 1760m, "USD", 9.57m);
    private static readonly SalaryCalculator.PayTerms Hourly = new(new(2026, 1, 1), PayBasis.Hourly, 20m, "USD", 20m);

    private static SalaryCalculator.Result Pay(
        SalaryCalculator.PayTerms terms,
        DateOnly joinedOn,
        IReadOnlyList<Work> work,
        bool ledger = true,
        params SalaryCalculator.Excuse[] excuses)
        => SalaryCalculator.Calculate(new SalaryCalculator.Input(June, July10, joinedOn, ledger, [terms], Calendar(), work, excuses))!;

    [Fact]
    public void A_personal_schedule_replaces_the_company_week_until_it_returns_to_the_default()
    {
        var calendar = Calendar(
            new PersonCalendar.Schedule(new(2026, 6, 8), false, new Dictionary<int, (int, int)> { [6] = (600, 900) }),
            new PersonCalendar.Schedule(new(2026, 6, 15), true, new Dictionary<int, (int, int)>()));

        calendar.HoursOn(new(2026, 6, 1))!.Required.ShouldBe(420);
        calendar.HoursOn(new(2026, 6, 6)).ShouldBeNull();
        calendar.HoursOn(new(2026, 6, 8)).ShouldBeNull();
        calendar.HoursOn(new(2026, 6, 13))!.Required.ShouldBe(300);
        calendar.HoursOn(new(2026, 6, 15))!.Start.ShouldBe(540);
    }

    [Fact]
    public void Overtime_is_time_outside_hours_on_days_off_and_beyond_the_required_hours_from_the_latest_work()
    {
        var splits = Overtime.Of(
            [
                At(1, 1, 8, 2),   // 08:00-10:00: one hour before the start
                At(2, 1, 10, 7),  // 10:00-17:00: inside, but the day now has 8h inside against 7h required
                At(3, 6, 10, 3),  // Saturday: a day off
            ],
            Calendar());

        splits.Single(s => s.Id == 1).Overtime.ShouldBe(60);
        splits.Single(s => s.Id == 2).Overtime.ShouldBe(60);
        splits.Single(s => s.Id == 3).Overtime.ShouldBe(180);
    }

    [Fact]
    public void Overtime_makes_up_a_shortfall_before_it_is_paid()
    {
        // Joined on Monday the 29th: two working days (840 minutes owed). Worked 8h on the 29th only.
        var result = Pay(Monthly, new(2026, 6, 29), [At(1, 29, 9, 8)]);

        var ledger = result.Ledger!;
        ledger.Expected.ShouldBe(840);
        ledger.Normal.ShouldBe(420);
        ledger.MadeUp.ShouldBe(60);
        ledger.Short.ShouldBe(360);
        ledger.Flagged.ShouldBeTrue();
        ledger.MissedDays.ShouldBe([new DateOnly(2026, 6, 30)]);
        result.OvertimePay.ShouldBe(0m);
    }

    [Fact]
    public void A_salaried_joiner_is_pro_rated_on_their_working_days_and_loses_the_shortfall()
    {
        var result = Pay(Monthly, new(2026, 6, 29), [At(1, 29, 9, 8)]);

        result.ProRated.ShouldBeTrue();
        result.WorkingDays.ShouldBe(22);
        result.WorkingDaysPaid.ShouldBe(2);

        // 1,760 x 2/22 = 160, less 6h short at 10 (the missed day was partly made up, so no break is lost).
        result.HourlyRate.ShouldBe(10m);
        result.DeductionMinutes.ShouldBe(360);
        result.BasePay.ShouldBe(100m);
    }

    [Fact]
    public void A_missed_day_costs_the_full_paid_day()
    {
        // Joined on the 30th and never worked: 7h short, plus the day's paid break.
        var result = Pay(Monthly, new(2026, 6, 30), []);

        result.Ledger!.Short.ShouldBe(420);
        result.DeductionMinutes.ShouldBe(480);
        result.Deduction.ShouldBe(80m); // 8h at 10: exactly the day's share of the salary (1,760 / 22)
        result.BasePay.ShouldBe(0m);
    }

    [Fact]
    public void A_salary_is_the_same_every_month_but_its_hours_are_worth_what_the_month_gives()
    {
        // July 2026 has 23 working days (184 paid hours), February 20 (160).
        var july = SalaryCalculator.Calculate(new SalaryCalculator.Input(
            new(2026, 7, 1), new(2026, 8, 10), new(2026, 1, 1), true, [Monthly], Calendar(), [], []))!;
        var february = SalaryCalculator.Calculate(new SalaryCalculator.Input(
            new(2026, 2, 1), new(2026, 8, 10), new(2026, 1, 1), false, [Monthly], Calendar(), [], []))!;

        july.HourlyRate.ShouldBe(9.5652m);     // 1,760 / 184
        february.HourlyRate.ShouldBe(11m);     // 1,760 / 160
        february.BasePay.ShouldBe(1760m);

        // Every working day of July missed: 23 full paid days, which is the whole salary.
        july.DeductionMinutes.ShouldBe(184 * 60);
        july.Deduction.ShouldBe(1760m);
        july.BasePay.ShouldBe(0m);
    }

    [Fact]
    public void Excused_days_are_not_short_and_unpaid_ones_are_deducted_as_full_days()
    {
        var paid = Pay(Monthly, new(2026, 6, 30), [], excuses: new SalaryCalculator.Excuse(ExcuseKind.Days, null, [new(2026, 6, 30)], true));
        paid.Ledger!.Short.ShouldBe(0);
        paid.Deduction.ShouldBe(0m);

        var unpaid = Pay(Monthly, new(2026, 6, 29), [At(1, 29, 9, 7)], excuses: new SalaryCalculator.Excuse(ExcuseKind.Days, null, [new(2026, 6, 30)], false));
        unpaid.Ledger!.Short.ShouldBe(0);
        unpaid.DeductionMinutes.ShouldBe(480);
        unpaid.Deduction.ShouldBe(80m);
    }

    [Fact]
    public void Hourly_staff_are_paid_what_they_log_plus_the_paid_break_and_overtime_at_the_multiplier()
    {
        // A full month of 8h days would be needed to owe nothing; here, one 8h day with no ledger.
        var result = Pay(Hourly, new(2026, 1, 1), [At(1, 1, 9, 8)], ledger: false);

        result.NormalMinutes.ShouldBe(420);
        result.OvertimeMinutes.ShouldBe(60);
        result.PaidBreakDays.ShouldBe(1);
        result.BasePay.ShouldBe(160m);      // 7h + the paid hour, at 20
        result.OvertimePay.ShouldBe(30m);   // 1h at 20 x 1.5
        result.Total.ShouldBe(190m);
    }

    [Fact]
    public void Salaried_overtime_is_paid_at_the_multiplier_when_nothing_is_short()
    {
        var result = Pay(Monthly, new(2026, 1, 1), [At(1, 6, 10, 2)], ledger: false);

        // Overtime is paid at the pay's overtime rate, whatever the month: 9.57 (1,760 / 184), not June's 10.
        result.BasePay.ShouldBe(1760m);
        result.OvertimeRate.ShouldBe(9.57m);
        result.OvertimePay.ShouldBe(28.71m); // 2h on a Saturday at 9.57 x 1.5
    }

    [Fact]
    public void An_overtime_rate_set_by_hand_is_what_overtime_is_paid_at()
    {
        var result = Pay(Monthly with { OvertimeRate = 12m }, new(2026, 1, 1), [At(1, 6, 10, 2)], ledger: false);

        result.OvertimePay.ShouldBe(36m); // 2h at 12 x 1.5
    }

    [Fact]
    public void No_pay_record_or_joining_after_the_month_means_no_salary()
    {
        SalaryCalculator.Calculate(new SalaryCalculator.Input(June, July10, new(2026, 1, 1), false, [], Calendar(), [], [])).ShouldBeNull();
        SalaryCalculator.Calculate(new SalaryCalculator.Input(June, July10, new(2026, 7, 1), false, [Monthly], Calendar(), [], [])).ShouldBeNull();
    }

    [Fact]
    public void Days_away_between_two_periods_of_employment_are_neither_paid_nor_expected()
    {
        // Away from Monday 8 June to Friday 19 June: 10 of June's 22 working days.
        IReadOnlyList<Employment.Gap> away = [new(new(2026, 6, 8), new(2026, 6, 19))];
        var input = new SalaryCalculator.Input(June, July10, new(2026, 1, 1), true, [Monthly], Calendar(), [], [], Gaps: away);

        var pay = SalaryCalculator.Calculate(input)!;
        pay.ProRated.ShouldBeTrue();
        pay.WorkingDays.ShouldBe(22);
        pay.WorkingDaysPaid.ShouldBe(12);
        pay.Ledger!.Expected.ShouldBe(12 * 420);

        // A month spent away entirely isn't paid at all.
        IReadOnlyList<Employment.Gap> allJune = [new(new(2026, 5, 20), new(2026, 7, 5))];
        SalaryCalculator.Calculate(input with { Gaps = allJune }).ShouldBeNull();
    }

    [Fact]
    public void A_fixed_salary_month_pays_the_salary_whatever_was_logged()
    {
        // An admin on a day of June: the whole month is a fixed salary, so 8 hours on Saturday 6 June earn no overtime.
        Work[] saturday = [At(1, 6, 9, 8)];
        var input = new SalaryCalculator.Input(June, July10, new(2026, 1, 1), false, [Monthly], Calendar(), saturday, [], FixedSalary: true);

        var pay = SalaryCalculator.Calculate(input)!;
        pay.OvertimePay.ShouldBe(0m);
        pay.OvertimeMinutes.ShouldBe(0);
        pay.Total.ShouldBe(1760m);
        SalaryCalculator.Calculate(input with { FixedSalary = false })!.OvertimePay.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public void Paid_leave_is_what_a_salary_pays_for_paid_time_off_as_unpaid_time_off_is_deducted()
    {
        // 2 paid hours and a paid day off (Tuesday 16 June, 8 paid hours) at 10 an hour; the unpaid hour is deducted instead
        SalaryCalculator.Excuse[] excuses =
        [
            new(ExcuseKind.Minutes, 120, [], true),
            new(ExcuseKind.Days, null, [new DateOnly(2026, 6, 16)], true),
            new(ExcuseKind.Minutes, 60, [], false),
        ];
        var input = new SalaryCalculator.Input(June, July10, new(2026, 1, 1), true, [Monthly], Calendar(), [], excuses);

        SalaryCalculator.PaidLeavePay(input, SalaryCalculator.Calculate(input)!).ShouldBe(100m);
    }

    [Fact]
    public void Paid_leave_is_a_whole_month_s_base_pay_and_paid_time_off_on_any_pay_but_never_on_a_fixed_salary()
    {
        SalaryCalculator.Input With(SalaryCalculator.PayTerms terms, bool fixedSalary, SalaryCalculator.Excuse excuse)
            => new(June, July10, new(2026, 1, 1), !fixedSalary, [terms], Calendar(), [], [excuse], FixedSalary: fixedSalary);
        decimal PaidLeave(SalaryCalculator.Input input) => SalaryCalculator.PaidLeavePay(input, SalaryCalculator.Calculate(input)!);
        var twoHours = new SalaryCalculator.Excuse(ExcuseKind.Minutes, 120, [], true);
        // Tuesday 2 June, 8 paid hours: 80 on the salary (10 an hour), 160 on hourly pay (20 an hour)
        var dayOff = new SalaryCalculator.Excuse(ExcuseKind.Days, null, [new(2026, 6, 2)], true, TimeOff: true);

        PaidLeave(With(Monthly, false, new SalaryCalculator.Excuse(ExcuseKind.WholeMonth, null, [], true))).ShouldBe(1760m);
        PaidLeave(With(Monthly, false, dayOff)).ShouldBe(80m);
        PaidLeave(With(Hourly, false, dayOff)).ShouldBe(160m);
        // an excuse pays hourly staff nothing; a fixed salary is a company cost whole, its time off included
        PaidLeave(With(Hourly, false, twoHours)).ShouldBe(0m);
        PaidLeave(With(Monthly, true, twoHours)).ShouldBe(0m);
        PaidLeave(With(Monthly, true, dayOff)).ShouldBe(0m);
    }

    [Fact]
    public void Paid_time_off_pays_an_hourly_employee_their_normal_day_but_an_excuse_pays_nothing()
    {
        // Tuesday 2 and Wednesday 3 June off; Saturday the 6th is no working day, so it adds nothing
        var off = new SalaryCalculator.Excuse(ExcuseKind.Days, null, [new(2026, 6, 2), new(2026, 6, 3), new(2026, 6, 6)], true, TimeOff: true);
        var paid = Pay(Hourly, new(2026, 1, 1), [], ledger: false, off);
        paid.TimeOffDays.ShouldBe(2);
        paid.TimeOffPay.ShouldBe(320m);
        paid.BasePay.ShouldBe(320m);

        var unpaid = Pay(Hourly, new(2026, 1, 1), [], ledger: false, off with { IsPaid = false });
        unpaid.TimeOffDays.ShouldBe(2);
        unpaid.TimeOffPay.ShouldBe(0m);

        Pay(Hourly, new(2026, 1, 1), [], ledger: false, new SalaryCalculator.Excuse(ExcuseKind.Days, null, [new(2026, 6, 2)], true))
            .BasePay.ShouldBe(0m);
    }

    [Fact]
    public void On_a_salary_paid_time_off_costs_nothing_and_unpaid_time_off_is_deducted_as_full_days()
    {
        // joined on Monday the 29th: the 29th and 30th are the month's two working days, both off
        var off = new SalaryCalculator.Excuse(ExcuseKind.Days, null, [new(2026, 6, 29), new(2026, 6, 30)], true, TimeOff: true);
        var paid = Pay(Monthly, new(2026, 6, 29), [], excuses: off);
        paid.Ledger!.Short.ShouldBe(0);
        paid.Deduction.ShouldBe(0m);
        paid.TimeOffPay.ShouldBe(0m);
        paid.BasePay.ShouldBe(160m);

        var unpaid = Pay(Monthly, new(2026, 6, 29), [], excuses: off with { IsPaid = false });
        unpaid.Ledger!.Short.ShouldBe(0);
        unpaid.DeductionMinutes.ShouldBe(960);
        unpaid.BasePay.ShouldBe(0m);
    }
}
