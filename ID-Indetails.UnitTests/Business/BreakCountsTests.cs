using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;
using Shouldly;
using Work = ID_Indetails.api.Business.Payroll.Overtime.Work;

namespace ID_Indetails.UnitTests.Business;

/// <summary>
/// From 1 December 2026 the break counts in people's hours (decided 24 November 2026). On a 9 to 5 with a paid hour of
/// break: a month expects 8 hours a day; 7 hours logged inside the hours take the break, which is added to what was
/// worked and carried by the day's projects; a full 9 to 5 logged worked the break, so it is 8 hours of normal work
/// and no break; 7 inside plus 1 after is the break taken and an hour of real overtime. December 2026 has 23 working
/// days, Monday to Friday: 184 hours expected.
/// </summary>
public class BreakCountsTests
{
    private const byte MondayToFriday = 62;
    private static readonly DateOnly December = new(2026, 12, 1);
    private static readonly DateOnly Today = new(2027, 1, 10);
    private static readonly DateOnly Tuesday = new(2026, 12, 8);
    private static readonly DateOnly NovemberTuesday = new(2026, 11, 24);

    private static readonly PersonCalendar Calendar = new(
        [
            new PersonCalendar.Policy(new(2000, 1, 1), MondayToFriday, 540, 1020, 60, 1.5m),
            new PersonCalendar.Policy(new(2026, 11, 1), MondayToFriday, 540, 1020, 60, 1.5m, 184),
        ],
        []);

    // 1,840 a month: overtime at 10 an hour, × 1.5
    private static readonly SalaryCalculator.PayTerms Monthly = new(new(2026, 1, 1), 1840m, "USD", 10m);

    private static long _id;

    /// <summary>One entry a working day of December from 09:00, <paramref name="minutes"/> long, plus <paramref name="after"/> from 17:00.</summary>
    private static List<Work> EveryDay(int minutes, int after = 0, int skip = 0)
        => HoursLedger.Days(December, December.AddMonths(1))
            .Where(d => Calendar.HoursOn(d) is not null)
            .Skip(skip)
            .SelectMany(d => after > 0
                ? new[] { new Work(++_id, d, 540, minutes), new Work(++_id, d, 1020, after) }
                : [new Work(++_id, d, 540, minutes)])
            .ToList();

    private static SalaryCalculator.Result Pay(IReadOnlyList<Work> work)
        => SalaryCalculator.Calculate(new SalaryCalculator.Input(December, Today, new(2026, 1, 1), true, [Monthly], Calendar, work, []))!;

    [Fact]
    public void A_full_9_to_5_is_the_break_worked_not_overtime_from_December()
    {
        Overtime.Of([new Work(1, Tuesday, 540, 480)], Calendar).Single().Overtime.ShouldBe(0);
        // 7 inside and 1 after: the hour after is real overtime
        Overtime.Of([new Work(1, Tuesday, 540, 420), new Work(2, Tuesday, 1020, 60)], Calendar).Sum(s => s.Overtime).ShouldBe(60);
        // before December the hour inside past the 7 required is overtime, as it was
        Overtime.Of([new Work(1, NovemberTuesday, 540, 480)], Calendar).Single().Overtime.ShouldBe(60);
    }

    [Fact]
    public void Projects_carry_the_break_taken_none_when_it_was_worked()
    {
        PaidBreaks.Allocate([new PaidBreaks.Work(1, 10, Tuesday, 540, 420)], Calendar).ShouldBe([new PaidBreaks.Share(10, Tuesday, 60m)]);
        // half an hour of it worked: half an hour taken
        PaidBreaks.Allocate([new PaidBreaks.Work(1, 10, Tuesday, 540, 450)], Calendar).ShouldBe([new PaidBreaks.Share(10, Tuesday, 30m)]);
        // a full 9 to 5: no break taken, nothing left for the company either
        PaidBreaks.Allocate([new PaidBreaks.Work(1, 10, Tuesday, 540, 480)], Calendar).ShouldBeEmpty();
        PaidBreaks.Unshared([new PaidBreaks.Work(1, 10, Tuesday, 540, 480)], Calendar).ShouldBeEmpty();
        // 7 inside and 1 after: the whole break, the overtime takes none
        PaidBreaks.Allocate([new PaidBreaks.Work(1, 10, Tuesday, 540, 420), new PaidBreaks.Work(2, 20, Tuesday, 1020, 60)], Calendar)
            .ShouldBe([new PaidBreaks.Share(10, Tuesday, 60m)]);
        // before December a full 9 to 5 still shares the whole break
        PaidBreaks.Allocate([new PaidBreaks.Work(1, 10, NovemberTuesday, 540, 480)], Calendar).ShouldBe([new PaidBreaks.Share(10, NovemberTuesday, 60m)]);
    }

    [Fact]
    public void The_month_expects_8_hours_a_day_and_7_logged_with_the_break_meet_them()
    {
        var result = Pay(EveryDay(420));

        result.Ledger!.Expected.ShouldBe(184 * 60);
        result.Ledger.Breaks.ShouldBe(23 * 60);
        result.Ledger.Normal.ShouldBe(184 * 60);
        result.Ledger.Short.ShouldBe(0);
        result.CountedMinutes.ShouldBe(184 * 60);
        result.OvertimePaidMinutes.ShouldBe(0);
    }

    [Fact]
    public void A_full_9_to_5_every_day_is_184_hours_with_no_overtime()
    {
        var result = Pay(EveryDay(480));

        result.OvertimeMinutes.ShouldBe(0);
        result.Ledger!.Breaks.ShouldBe(0);
        result.Ledger.Normal.ShouldBe(184 * 60);
        result.CountedMinutes.ShouldBe(184 * 60);
        result.OvertimePay.ShouldBe(0m);
    }

    [Fact]
    public void Seven_inside_and_one_after_every_day_is_paid_23_hours_of_overtime()
    {
        var result = Pay(EveryDay(420, after: 60));

        // 23 × (7 worked + 1 break + 1 overtime) = 207 counted: 23 past 184
        result.CountedMinutes.ShouldBe(207 * 60);
        result.OvertimeMinutes.ShouldBe(23 * 60);
        result.OvertimePaidMinutes.ShouldBe(23 * 60);
        result.OvertimePay.ShouldBe(23 * 10m * 1.5m);
    }

    [Fact]
    public void A_short_day_counts_its_part_of_the_break_and_the_company_covers_the_rest()
    {
        // every day 7 hours but one of 5: 5/7 of its break taken (43 minutes), 17 covered; 2 hours short, as before
        var work = EveryDay(420, skip: 1);
        work.Add(new Work(++_id, December, 540, 300));
        var ledger = Pay(work).Ledger!;

        ledger.Breaks.ShouldBe(22 * 60 + 43);
        ledger.BreaksCovered.ShouldBe(17);
        ledger.Short.ShouldBe(120);
    }

    [Fact]
    public void Excused_hours_bring_their_share_of_the_break()
    {
        // a missed day with 3 hours excused: 3 hours and 26 minutes covered (3/7 of the hour), so 4 hours 34 minutes short
        var result = SalaryCalculator.Calculate(new SalaryCalculator.Input(December, Today, new(2026, 1, 1), true, [Monthly], Calendar,
            EveryDay(420, skip: 1), [new SalaryCalculator.Excuse(ExcuseKind.Minutes, 180, [], IsPaid: true)]))!;

        result.Ledger!.Excused.ShouldBe(180 + 26);
        result.Ledger.Short.ShouldBe(480 - 206);
        HoursLedger.BreakShareOf(180, [new WorkHours(540, 1020, 60, BreakCounts: false)]).ShouldBe(0);
    }

    [Fact]
    public void A_missed_day_costs_its_8_hours_once()
    {
        var result = Pay(EveryDay(420, skip: 1));

        result.Ledger!.Short.ShouldBe(480);
        result.DeductionMinutes.ShouldBe(480);
    }
}
