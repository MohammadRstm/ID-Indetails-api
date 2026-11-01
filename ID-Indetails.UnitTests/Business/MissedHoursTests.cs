using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class MissedHoursTests
{
    // Monday to Friday, 9 to 5 with a paid hour's break: 7 hours required a day.
    private static readonly PersonCalendar Calendar = new(
        [new PersonCalendar.Policy(new DateOnly(2020, 1, 1), WorkWeek.ToMask([1, 2, 3, 4, 5]), 540, 1020, 60, 1m)],
        []);

    private static readonly DateOnly Tuesday = new(2026, 10, 27);
    private static readonly Employment Employed = new([new Employment.Period(new DateOnly(2025, 1, 1), null)]);

    private static MissedHours.Short? Check(DateOnly day, int logged, Employment? employment = null, params SalaryCalculator.Excuse[] excuses)
        => MissedHours.Check(day, Calendar, employment ?? Employed, excuses, logged);

    [Fact]
    public void The_morning_after_is_when_a_day_is_judged()
    {
        MissedHours.DayToCheck(new DateTime(2026, 10, 28, 9, 0, 0)).ShouldBe(Tuesday);
        MissedHours.DayToCheck(new DateTime(2026, 10, 28, 23, 59, 0)).ShouldBe(Tuesday);
        // before 09:00 yesterday may still be logged, so the day before is judged
        MissedHours.DayToCheck(new DateTime(2026, 10, 28, 8, 59, 0)).ShouldBe(Tuesday.AddDays(-1));
    }

    [Fact]
    public void Logging_less_than_the_day_requires_is_missed()
    {
        Check(Tuesday, logged: 180).ShouldBe(new MissedHours.Short(180, 420));
        Check(Tuesday, logged: 0).ShouldBe(new MissedHours.Short(0, 420));
    }

    [Fact]
    public void Every_minute_logged_that_day_counts_overtime_included()
    {
        Check(Tuesday, logged: 420).ShouldBeNull();
        Check(Tuesday, logged: 600).ShouldBeNull();
    }

    [Fact]
    public void A_day_off_asks_for_nothing()
    {
        Check(new DateOnly(2026, 10, 25), logged: 0).ShouldBeNull();
    }

    [Fact]
    public void An_excused_day_or_month_is_never_missed_but_excused_minutes_name_no_day()
    {
        Check(Tuesday, 0, null, new SalaryCalculator.Excuse(ExcuseKind.Days, null, [Tuesday], true)).ShouldBeNull();
        Check(Tuesday, 0, null, new SalaryCalculator.Excuse(ExcuseKind.WholeMonth, null, [], false)).ShouldBeNull();
        Check(Tuesday, 0, null, new SalaryCalculator.Excuse(ExcuseKind.Days, null, [Tuesday.AddDays(1)], true)).ShouldNotBeNull();
        Check(Tuesday, 0, null, new SalaryCalculator.Excuse(ExcuseKind.Minutes, 420, [], true)).ShouldNotBeNull();
    }

    [Fact]
    public void Days_outside_their_employment_are_never_missed()
    {
        Check(Tuesday, 0, new Employment([new Employment.Period(Tuesday.AddDays(1), null)])).ShouldBeNull();
        Check(Tuesday, 0, new Employment([new Employment.Period(new DateOnly(2025, 1, 1), Tuesday.AddDays(-1))])).ShouldBeNull();
        Check(Tuesday, 0, new Employment([
            new Employment.Period(new DateOnly(2025, 1, 1), Tuesday.AddDays(-3)),
            new Employment.Period(Tuesday.AddDays(2), null),
        ])).ShouldBeNull();
        Check(Tuesday, 0, new Employment([new Employment.Period(Tuesday, Tuesday)])).ShouldNotBeNull();
    }
}
