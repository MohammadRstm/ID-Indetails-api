using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Enums;
using Shouldly;
using E = ID_Indetails.api.Business.Payroll.ExcuseTrim.Excuse;

namespace ID_Indetails.UnitTests.Business;

public class ExcuseTrimTests
{
    private static readonly DateTime Earlier = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Tue = new(2026, 10, 13), Wed = new(2026, 10, 14);

    // seven required hours a working day
    private static int Required(DateOnly day) => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0 : 420;

    [Fact]
    public void A_day_excuse_loses_the_days_the_work_falls_on_and_goes_once_it_has_none()
    {
        var both = new E(1, ExcuseKind.Days, null, [Tue, Wed], Earlier);
        var change = ExcuseTrim.Of([both], new HashSet<DateOnly> { Tue }, 180, 22 * 420, Required).ShouldHaveSingleItem();
        change.Removed.ShouldBeFalse();
        change.Days.ShouldBe([Wed]);
        change.DaysTaken.ShouldBe([Tue]);

        var one = new E(2, ExcuseKind.Days, null, [Tue], Earlier);
        ExcuseTrim.Of([one], new HashSet<DateOnly> { Tue }, 180, 22 * 420, Required).ShouldHaveSingleItem().Removed.ShouldBeTrue();
        ExcuseTrim.Of([one], new HashSet<DateOnly> { Wed }, 180, 22 * 420, Required).ShouldBeEmpty();
    }

    [Fact]
    public void Minute_excuses_lose_only_what_the_work_within_hours_makes_redundant_oldest_first()
    {
        var older = new E(1, ExcuseKind.Minutes, 120, [], Earlier);
        var newer = new E(2, ExcuseKind.Minutes, 60, [], Earlier.AddDays(1));

        // the month still needs the excused time: nothing changes
        ExcuseTrim.Of([older, newer], new HashSet<DateOnly>(), 1000, 2000, Required).ShouldBeEmpty();

        // work within hours now covers all but 30 minutes of the month: 150 of the 180 excused are redundant
        var changes = ExcuseTrim.Of([newer, older], new HashSet<DateOnly>(), 1970, 2000, Required);
        changes.Select(c => (c.ExcuseId, c.Removed, c.Minutes, c.MinutesTaken)).ShouldBe([(1, true, 0, 120), (2, false, 30, 30)]);
    }

    [Fact]
    public void A_whole_month_excuse_changes_nothing()
    {
        var whole = new E(1, ExcuseKind.WholeMonth, null, [], Earlier);
        var day = new E(2, ExcuseKind.Days, null, [Tue], Earlier);
        ExcuseTrim.Of([whole, day], new HashSet<DateOnly> { Tue }, 9000, 2000, Required).ShouldBeEmpty();
    }
}
