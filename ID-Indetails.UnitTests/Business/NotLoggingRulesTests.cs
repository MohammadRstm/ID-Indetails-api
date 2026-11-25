using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.Reports;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using Shouldly;
using Work = ID_Indetails.api.Business.Payroll.Overtime.Work;

namespace ID_Indetails.UnitTests.Business;

/// <summary>
/// The people the dashboard counts as not logging this week, and their hours this week from the hours ledger's
/// calculators. A 9 to 5 with a paid hour's break expects 7 hours a day before 1 December 2026 and 8 from then, when
/// the break counts, and a full 9 to 5 from then is no overtime.
/// </summary>
public class NotLoggingRulesTests
{
    private const byte MondayToFriday = 62;

    private static readonly PersonCalendar Calendar = new(
        [new PersonCalendar.Policy(new(2000, 1, 1), MondayToFriday, 540, 1020, 60, 1.5m)],
        []);

    // Wednesday 25 November 2026: the week began on Monday the 23rd, a week before the break counts.
    private static readonly DateOnly NovemberWeek = new(2026, 11, 23);
    private static readonly DateOnly NovemberToday = new(2026, 11, 25);

    // Friday 4 December 2026: the week began on Monday 30 November, so it spans November and December.
    private static readonly DateOnly SplitWeek = new(2026, 11, 30);
    private static readonly DateOnly SplitToday = new(2026, 12, 4);

    private static readonly DateOnly Joined = new(2026, 1, 1);

    private static SalaryCalculator.Input Month(
        DateOnly month,
        DateOnly today,
        IReadOnlyList<Work>? work = null,
        IReadOnlyList<SalaryCalculator.Excuse>? excuses = null,
        DateOnly? joined = null,
        bool ledgerApplies = true)
        => new(month, today, joined ?? Joined, ledgerApplies, [], Calendar, work ?? [], excuses ?? []);

    private static DesignPerson Person(long id, string name, string role = RoleCodes.Employee, UserStatus status = UserStatus.Active)
        => new(id, name, $"{name}@example.com".ToLowerInvariant(), null, null, null, role, status, null, null);

    [Fact]
    public void Only_active_employees_who_logged_nothing_are_listed_by_name()
    {
        var people = new[]
        {
            Person(1, "Zaid"),
            Person(2, "Anna"),
            Person(3, "Ben"),
            Person(4, "Admin", RoleCodes.Admin),
            Person(5, "Invited", status: UserStatus.Invited),
        };

        NotLoggingRules.Of(people, new HashSet<long> { 3 }).Select(p => p.Id).ShouldBe([2, 1]);
    }

    [Fact]
    public void Before_the_break_counts_a_week_expects_7_hours_a_working_day()
    {
        var week = NotLoggingRules.HoursOf(NovemberWeek, NovemberToday, [Month(new(2026, 11, 1), NovemberToday)]);

        // Monday to Wednesday
        week.ShouldBe(new NotLoggingRules.WeekHours(0, 3 * 7 * 60, 0));
    }

    [Fact]
    public void From_December_a_week_expects_8_hours_a_working_day_and_a_week_can_span_two_months()
    {
        var week = NotLoggingRules.HoursOf(SplitWeek, SplitToday,
        [
            Month(new(2026, 11, 1), SplitToday),
            Month(new(2026, 12, 1), SplitToday),
        ]);

        // Monday 30 November still expects its required 7 hours; Tuesday to Friday expect the whole 9 to 5
        week.Expected.ShouldBe((7 * 60) + (4 * 8 * 60));
        week.Logged.ShouldBe(0);
        week.Overtime.ShouldBe(0);
    }

    [Fact]
    public void A_full_9_to_5_from_December_is_logged_with_no_overtime()
    {
        var tuesday = new DateOnly(2026, 12, 1);
        var week = NotLoggingRules.HoursOf(SplitWeek, SplitToday,
        [
            Month(new(2026, 11, 1), SplitToday),
            Month(new(2026, 12, 1), SplitToday, [new Work(1, tuesday, 540, 480)]),
        ]);

        week.Logged.ShouldBe(480);
        week.Overtime.ShouldBe(0);
    }

    [Fact]
    public void Seven_hours_from_December_count_the_break_taken_as_logged()
    {
        var tuesday = new DateOnly(2026, 12, 1);
        var week = NotLoggingRules.HoursOf(SplitWeek, SplitToday,
        [
            Month(new(2026, 12, 1), SplitToday, [new Work(1, tuesday, 540, 420)]),
        ]);

        // the 7 hours worked and the hour of break they took, as the hours ledger counts them
        week.Logged.ShouldBe(480);
        week.Overtime.ShouldBe(0);
    }

    [Fact]
    public void Before_December_the_hour_inside_past_the_required_ones_is_still_overtime()
    {
        var tuesday = new DateOnly(2026, 11, 24);
        var week = NotLoggingRules.HoursOf(NovemberWeek, NovemberToday,
            [Month(new(2026, 11, 1), NovemberToday, [new Work(1, tuesday, 540, 480)])]);

        week.Logged.ShouldBe(480);
        week.Overtime.ShouldBe(60);
    }

    [Fact]
    public void Work_outside_the_week_is_not_counted()
    {
        var before = new DateOnly(2026, 11, 20);
        var week = NotLoggingRules.HoursOf(NovemberWeek, NovemberToday,
            [Month(new(2026, 11, 1), NovemberToday, [new Work(1, before, 540, 600)])]);

        week.Logged.ShouldBe(0);
        week.Overtime.ShouldBe(0);
    }

    [Fact]
    public void An_excused_day_is_not_expected()
    {
        var monday = NovemberWeek;
        var excuse = new SalaryCalculator.Excuse(ExcuseKind.Days, null, [monday], IsPaid: true);

        NotLoggingRules.HoursOf(NovemberWeek, NovemberToday, [Month(new(2026, 11, 1), NovemberToday, excuses: [excuse])]).Expected
            .ShouldBe(2 * 7 * 60);
        // a whole month excused leaves nothing to log
        var whole = new SalaryCalculator.Excuse(ExcuseKind.WholeMonth, null, [], IsPaid: true);
        NotLoggingRules.HoursOf(NovemberWeek, NovemberToday, [Month(new(2026, 11, 1), NovemberToday, excuses: [whole])]).Expected
            .ShouldBe(0);
    }

    [Fact]
    public void Days_before_they_joined_are_not_expected_and_a_month_without_a_ledger_counts_nothing()
    {
        var joinedTuesday = new DateOnly(2026, 11, 24);

        NotLoggingRules.HoursOf(NovemberWeek, NovemberToday, [Month(new(2026, 11, 1), NovemberToday, joined: joinedTuesday)]).Expected
            .ShouldBe(2 * 7 * 60);
        NotLoggingRules.HoursOf(NovemberWeek, NovemberToday, [Month(new(2026, 11, 1), NovemberToday, ledgerApplies: false)])
            .ShouldBe(new NotLoggingRules.WeekHours(0, 0, 0));
    }

    [Fact]
    public void The_week_expects_what_the_months_ledger_does_for_the_same_days()
    {
        // the same days through the month's own ledger: from the 23rd to today is the whole of what November has so far
        var today = NovemberToday;
        var ledger = HoursLedger.Build(new(2026, 11, 1), today, new(2026, 11, 23), Calendar, [], new HoursLedger.Excuses(false, 0, new HashSet<DateOnly>()));
        var week = NotLoggingRules.HoursOf(NovemberWeek, NovemberToday, [Month(new(2026, 11, 1), NovemberToday, joined: new(2026, 11, 23))]);

        // Build judges a month in progress up to the day before today's end; the week counts to today inclusive
        week.Expected.ShouldBe(ledger.Expected + (7 * 60));
    }
}
