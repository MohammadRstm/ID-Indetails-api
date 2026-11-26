using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;
using Shouldly;
using Work = ID_Indetails.api.Business.Payroll.Overtime.Work;

namespace ID_Indetails.UnitTests.Business;

/// <summary>
/// Hours someone logged on a day of their time off, approved by a super admin (decided 26 November 2026): as overtime,
/// every minute of the entry is overtime, paid under the month's threshold; as normal hours, they are plain normal hours.
/// A 9 to 5 with a paid hour of break, Monday to Friday; December 2026 has 23 working days, 184 hours expected.
/// </summary>
public class TimeOffOvertimeTests
{
    private const byte MondayToFriday = 62;
    private static readonly DateOnly December = new(2026, 12, 1);
    private static readonly DateOnly Today = new(2027, 1, 10);
    private static readonly DateOnly Tuesday = new(2026, 12, 8);

    private static readonly PersonCalendar Calendar = new(
        [new PersonCalendar.Policy(new(2026, 11, 1), MondayToFriday, 540, 1020, 60, 1.5m, 184)],
        []);

    // 1,840 a month: overtime at 10 an hour, × 1.5
    private static readonly SalaryCalculator.PayTerms Monthly = new(new(2026, 1, 1), 1840m, "USD", 10m);

    [Fact]
    public void Approved_as_overtime_every_minute_is_overtime_even_inside_the_hours()
    {
        Overtime.Of([new Work(1, Tuesday, 540, 480, AllOvertime: true)], Calendar).Single().Overtime.ShouldBe(480);
        // approved as normal hours: a 9 to 5 is normal work, as on any day
        Overtime.Of([new Work(1, Tuesday, 540, 480)], Calendar).Single().Overtime.ShouldBe(0);
    }

    [Fact]
    public void Hours_approved_as_overtime_leave_the_day_to_the_rest_of_its_work()
    {
        var splits = Overtime.Of([new Work(1, Tuesday, 540, 240, AllOvertime: true), new Work(2, Tuesday, 780, 240)], Calendar);

        splits.Single(s => s.Id == 1).Overtime.ShouldBe(240);
        splits.Single(s => s.Id == 2).Overtime.ShouldBe(0);
    }

    [Fact]
    public void Hours_approved_as_overtime_take_no_share_of_the_break()
    {
        PaidBreaks.Allocate([new PaidBreaks.Work(1, 10, Tuesday, 540, 420, AllOvertime: true)], Calendar).ShouldBeEmpty();
        PaidBreaks.Unshared([new PaidBreaks.Work(1, 10, Tuesday, 540, 420, AllOvertime: true)], Calendar).ShouldBeEmpty();
    }

    [Fact]
    public void Approved_as_overtime_on_a_paid_day_off_they_are_paid_past_the_threshold()
    {
        // every other working day a full 9 to 5, Tuesday a paid day off with 4 hours logged on it
        var pay = Pay(new Work(100, Tuesday, 540, 240, AllOvertime: true));

        // 22 × 8h + the paid day's 8h + the 4h = 188h counted, 4h past the 184h threshold
        pay.CountedMinutes.ShouldBe(188 * 60);
        pay.OvertimePaidMinutes.ShouldBe(240);
        pay.OvertimePay.ShouldBe(60m);
    }

    [Fact]
    public void Approved_as_normal_hours_on_a_day_off_they_earn_no_overtime()
    {
        var pay = Pay(new Work(100, Tuesday, 540, 240));

        pay.OvertimeMinutes.ShouldBe(0);
        pay.OvertimePay.ShouldBe(0m);
    }

    private static SalaryCalculator.Result Pay(Work onTheDayOff)
    {
        var work = HoursLedger.Days(December, December.AddMonths(1))
            .Where(d => Calendar.HoursOn(d) is not null && d != Tuesday)
            .Select((d, i) => new Work(i + 1, d, 540, 480))
            .Append(onTheDayOff)
            .ToList();
        var dayOff = new SalaryCalculator.Excuse(ExcuseKind.Days, null, [Tuesday], IsPaid: true, TimeOff: true);
        return SalaryCalculator.Calculate(new SalaryCalculator.Input(December, Today, new(2026, 1, 1), true, [Monthly], Calendar, work, [dayOff]))!;
    }
}
