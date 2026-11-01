using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;
using Shouldly;
using Work = ID_Indetails.api.Business.Payroll.Overtime.Work;

namespace ID_Indetails.UnitTests.Business;

/// <summary>
/// From 1 November 2026 a salary's overtime is paid only past 184 counted hours a month, or past the month's expected
/// hours when those are more. December 2026 has 23 Monday–Friday working days of 8 hours (the break is 0): 184 hours.
/// </summary>
public class OvertimeThresholdTests
{
    private static readonly DateOnly December = new(2026, 12, 1);
    private static readonly DateOnly Today = new(2027, 1, 10);
    private const byte MondayToFriday = 62;
    private const byte MondayToSaturday = 126;

    private static PersonCalendar CalendarOf(byte decemberDays = MondayToFriday, int threshold = 184) => new(
        [
            new PersonCalendar.Policy(new(2000, 1, 1), MondayToFriday, 540, 1020, 60, 1.5m),
            new PersonCalendar.Policy(new(2026, 11, 1), MondayToFriday, 540, 1020, 60, 1.5m, threshold),
            new PersonCalendar.Policy(December, decemberDays, 540, 1020, 0, 1.5m, threshold),
        ],
        []);

    // 1,840 a month: overtime at 10 an hour (the salary ÷ 184), × 1.5
    private static readonly SalaryCalculator.PayTerms Monthly = new(new(2026, 1, 1), PayBasis.Monthly, 1840m, "USD", 10m);
    private static readonly SalaryCalculator.PayTerms Hourly = new(new(2026, 1, 1), PayBasis.Hourly, 20m, "USD", 20m);

    private static long _id;

    /// <summary>9 to 5 on every working day of December from <paramref name="from"/>, except <paramref name="skip"/> of them.</summary>
    private static List<Work> FullDays(PersonCalendar calendar, DateOnly? from = null, int skip = 0)
        => HoursLedger.Days(from ?? December, December.AddMonths(1))
            .Where(d => calendar.HoursOn(d) is not null)
            .Skip(skip)
            .Select(d => new Work(++_id, d, 540, 480))
            .ToList();

    /// <summary>Hours on a Sunday (or Saturday, off Monday–Friday): all of it overtime.</summary>
    private static Work DayOff(int day, int hours) => new(++_id, new(2026, 12, day), 540, hours * 60);

    private static SalaryCalculator.Result Pay(
        PersonCalendar calendar,
        IReadOnlyList<Work> work,
        SalaryCalculator.PayTerms? terms = null,
        IReadOnlyList<SalaryCalculator.Excuse>? excuses = null,
        DateOnly? joinedOn = null)
        => SalaryCalculator.Calculate(new SalaryCalculator.Input(
            December, Today, joinedOn ?? new(2026, 1, 1), true, [terms ?? Monthly], calendar, work, excuses ?? []))!;

    [Fact]
    public void A_salary_is_paid_only_the_overtime_counted_past_184_hours()
    {
        var calendar = CalendarOf();
        var result = Pay(calendar, [.. FullDays(calendar), DayOff(6, 10)]);

        // 184 hours of full days plus 10 on a Sunday: 194 counted, all 10 past the threshold
        result.ThresholdMinutes.ShouldBe(184 * 60);
        result.CountedMinutes.ShouldBe(194 * 60);
        result.OvertimeMinutes.ShouldBe(10 * 60);
        result.OvertimePaidMinutes.ShouldBe(10 * 60);
        result.OvertimePay.ShouldBe(150m);
    }

    [Fact]
    public void Overtime_under_184_counted_hours_is_logged_but_not_paid()
    {
        var calendar = CalendarOf();
        // three working days missed, 10 hours on a Sunday: 160 + 10 = 170 counted
        var result = Pay(calendar, [.. FullDays(calendar, skip: 3), DayOff(6, 10)]);

        result.OvertimeMinutes.ShouldBe(10 * 60);
        result.OvertimePaidMinutes.ShouldBe(0);
        result.OvertimePay.ShouldBe(0m);
        // the overtime still makes up the short month
        result.MadeUpMinutes.ShouldBe(10 * 60);
    }

    [Fact]
    public void When_a_month_expects_more_than_184_hours_overtime_is_paid_only_past_them()
    {
        // Monday to Saturday: 27 working days of 8 hours, 216 expected
        var calendar = CalendarOf(MondayToSaturday);
        var result = Pay(calendar, [.. FullDays(calendar), DayOff(6, 5)]);

        result.ThresholdMinutes.ShouldBe(216 * 60);
        result.OvertimePaidMinutes.ShouldBe(5 * 60);
        result.OvertimePay.ShouldBe(75m);
    }

    [Fact]
    public void Paid_excuses_count_toward_184_hours_and_unpaid_ones_do_not()
    {
        var calendar = CalendarOf();
        var work = FullDays(calendar, skip: 1);
        var missed = HoursLedger.Days(December, December.AddMonths(1)).First(d => calendar.HoursOn(d) is not null);
        work.Add(DayOff(6, 10));

        // 176 worked + 8 excused + 10 on a Sunday
        Pay(calendar, work, excuses: [new(ExcuseKind.Days, null, [missed], IsPaid: true)]).OvertimePaidMinutes.ShouldBe(10 * 60);
        Pay(calendar, work, excuses: [new(ExcuseKind.Days, null, [missed], IsPaid: false)]).OvertimePaidMinutes.ShouldBe(2 * 60);
    }

    [Fact]
    public void Joining_mid_month_does_not_lower_the_threshold()
    {
        var calendar = CalendarOf();
        var joined = new DateOnly(2026, 12, 15);
        var result = Pay(calendar, [.. FullDays(calendar, joined), DayOff(20, 12)], joinedOn: joined);

        result.ThresholdMinutes.ShouldBe(184 * 60);
        result.OvertimePaidMinutes.ShouldBe(0);
        result.OvertimePay.ShouldBe(0m);
    }

    [Fact]
    public void The_paid_overtime_is_the_months_latest()
    {
        var calendar = CalendarOf();
        var first = DayOff(6, 4);
        var last = DayOff(27, 4);
        // 184 hours of full days plus 8 on two Sundays, two hours of them missing from the start of the month: 6 past
        var work = FullDays(calendar);
        work[0] = work[0] with { Minutes = 360 };

        var result = Pay(calendar, [.. work, first, last]);

        result.OvertimePaidMinutes.ShouldBe(6 * 60);
        result.PaidOvertimeByWork![last.Id].ShouldBe(4 * 60);
        result.PaidOvertimeByWork[first.Id].ShouldBe(2 * 60);
    }

    [Fact]
    public void Hourly_pay_and_months_without_a_threshold_pay_all_overtime_as_before()
    {
        var calendar = CalendarOf();
        var hourly = Pay(calendar, [.. FullDays(calendar), DayOff(6, 10)], Hourly);
        hourly.ThresholdMinutes.ShouldBeNull();
        hourly.OvertimePay.ShouldBe(300m);

        var off = CalendarOf(threshold: 0);
        var salary = Pay(off, [.. FullDays(off, skip: 3), DayOff(6, 10)]);
        salary.ThresholdMinutes.ShouldBeNull();
        // the old rule: the 10 hours make up the 24 missing, none is left to pay as overtime
        salary.OvertimePaidMinutes.ShouldBe(0);
        Pay(off, [.. FullDays(off), DayOff(6, 10)]).OvertimePay.ShouldBe(150m);
    }

    [Fact]
    public void Has_threshold_answers_as_the_full_calculation_does()
    {
        var on = CalendarOf();
        var off = CalendarOf(threshold: 0);
        var cases = new List<SalaryCalculator.Input>();
        foreach (var calendar in new[] { on, off })
        {
            foreach (var terms in new SalaryCalculator.PayTerms?[] { Monthly, Hourly, null })
            {
                foreach (var fixedSalary in new[] { false, true })
                {
                    SalaryCalculator.Input Of(DateOnly joinedOn, DateOnly? leftOn = null) => new(
                        December, Today, joinedOn, true, terms is null ? [] : [terms], calendar,
                        [.. FullDays(calendar), DayOff(6, 10)], [], leftOn, null, fixedSalary);
                    cases.Add(Of(new(2026, 1, 1)));
                    // joined after the month, or left before it: not employed on any of its days
                    cases.Add(Of(new(2027, 1, 4)));
                    cases.Add(Of(new(2026, 1, 1), new(2026, 11, 30)));
                    // joined mid-month
                    cases.Add(Of(new(2026, 12, 14)));
                }
            }
        }

        // pay set only from after the month: no terms on its days
        cases.Add(new(December, Today, new(2026, 1, 1), true, [Monthly with { EffectiveFrom = new(2027, 2, 1) }], on, [], []));

        foreach (var input in cases)
        {
            SalaryCalculator.HasThreshold(input).ShouldBe(SalaryCalculator.Calculate(input)?.ThresholdMinutes is not null);
        }

        cases.Count(SalaryCalculator.HasThreshold).ShouldBe(2);
    }

    [Fact]
    public void A_salarys_suggested_overtime_rate_follows_the_threshold()
    {
        PayRules.SuggestedOvertimeRate(PayBasis.Monthly, 1840m, 184).ShouldBe(10m);
        PayRules.SuggestedOvertimeRate(PayBasis.Monthly, 1840m, 200).ShouldBe(9.2m);
        PayRules.SuggestedOvertimeRate(PayBasis.Monthly, 1840m).ShouldBe(10m);
        PayRules.SuggestedOvertimeRate(PayBasis.Hourly, 20m, 200).ShouldBe(20m);
    }
}
