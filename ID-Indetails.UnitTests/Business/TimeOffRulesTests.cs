using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.TimeOff;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using Period = ID_Indetails.api.Business.TimeOff.TimeOffRules.Period;

namespace ID_Indetails.UnitTests.Business;

/// <summary>Monday 9 November 2026 is "today".</summary>
public class TimeOffRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static Period Approved(int first, int last, bool paid = true)
        => new(1, new(2026, 11, first), new(2026, 11, last), TimeOffStatus.Approved, null, paid);

    private static Period Rejected(int first, int last, int on)
        => new(2, new(2026, 11, first), new(2026, 11, last), TimeOffStatus.Rejected, new(2026, 11, on), true);

    private static string? Request(DateOnly first, DateOnly last, bool working = true, bool overlaps = false, bool excused = false)
        => TimeOffRules.CheckRequest(first, last, Today, working, overlaps, excused)?.Code;

    [Fact]
    public void Time_off_starts_tomorrow_at_the_earliest_and_at_most_six_months_ahead()
    {
        Request(Today, Today).ShouldBe(ErrorCodes.TimeOffTooSoon);
        Request(Today.AddDays(1), Today.AddDays(1)).ShouldBeNull();
        Request(Today.AddMonths(6), Today.AddMonths(6)).ShouldBeNull();
        Request(Today.AddMonths(6).AddDays(1), Today.AddMonths(6).AddDays(1)).ShouldBe(ErrorCodes.TimeOffTooFar);
    }

    [Fact]
    public void A_request_needs_a_working_day_and_overlaps_neither_their_own_time_off_nor_an_excuse()
    {
        var day = Today.AddDays(3);
        Request(day, day.AddDays(-1)).ShouldBe(ErrorCodes.ValidationFailed);
        Request(day, day, working: false).ShouldBe(ErrorCodes.TimeOffNoWorkingDay);
        Request(day, day, overlaps: true).ShouldBe(ErrorCodes.TimeOffOverlaps);
        Request(day, day, excused: true).ShouldBe(ErrorCodes.TimeOffExcused);
    }

    [Fact]
    public void The_first_decision_wins_and_a_request_is_approved_only_before_its_first_day()
    {
        TimeOffRules.CheckApprove(TimeOffStatus.Pending, Today.AddDays(1), Today, false).ShouldBeNull();
        TimeOffRules.CheckApprove(TimeOffStatus.Approved, Today.AddDays(1), Today, false)!.Code.ShouldBe(ErrorCodes.TimeOffDecided);
        TimeOffRules.CheckApprove(TimeOffStatus.Withdrawn, Today.AddDays(1), Today, false)!.Code.ShouldBe(ErrorCodes.TimeOffDecided);
        TimeOffRules.CheckApprove(TimeOffStatus.Pending, Today, Today, false)!.Code.ShouldBe(ErrorCodes.TimeOffExpired);
        TimeOffRules.CheckApprove(TimeOffStatus.Pending, Today.AddDays(1), Today, true)!.Code.ShouldBe(ErrorCodes.TimeOffExcused);
        TimeOffRules.CheckPending(TimeOffStatus.Declined)!.Code.ShouldBe(ErrorCodes.TimeOffDecided);
    }

    private static RuleDenial? Change(
        DateOnly first,
        DateOnly last,
        TimeOffStatus status = TimeOffStatus.Approved,
        bool unchanged = false,
        bool working = true,
        Period? overlap = null,
        DateOnly[]? excused = null,
        DateOnly? excusedMonth = null,
        DateOnly[]? withHours = null)
        => TimeOffRules.CheckChange(status, first, last, Today, unchanged, working, overlap, excused ?? [], excusedMonth, withHours ?? []);

    [Fact]
    public void Approved_time_off_can_be_moved_into_the_past_but_not_past_six_months_ahead()
    {
        Change(Today.AddDays(-40), Today.AddDays(-35)).ShouldBeNull();
        Change(Today, Today.AddDays(2)).ShouldBeNull();
        Change(Today.AddMonths(6).AddDays(1), Today.AddMonths(6).AddDays(2))!.Code.ShouldBe(ErrorCodes.TimeOffTooFar);
        Change(Today, Today.AddDays(-1))!.Code.ShouldBe(ErrorCodes.ValidationFailed);
        Change(Today, Today, unchanged: true)!.Code.ShouldBe(ErrorCodes.ValidationFailed);
        Change(Today, Today, working: false)!.Code.ShouldBe(ErrorCodes.TimeOffNoWorkingDay);
        foreach (var status in new[] { TimeOffStatus.Pending, TimeOffStatus.Rejected, TimeOffStatus.Ended, TimeOffStatus.Declined })
        {
            Change(Today, Today, status)!.Code.ShouldBe(ErrorCodes.TimeOffNotApproved);
        }
    }

    [Fact]
    public void New_days_name_the_time_off_excuses_or_hours_they_would_clash_with()
    {
        var day = new DateOnly(2026, 11, 3);

        var overlap = Change(day, day, overlap: Rejected(2, 10, on: 5))!;
        overlap.Code.ShouldBe(ErrorCodes.TimeOffOverlaps);
        overlap.Message.ShouldContain("2 Nov 2026 to 4 Nov 2026");

        Change(day, day, excused: [day])!.Message.ShouldContain("3 Nov 2026 is already excused");
        Change(day, day, excusedMonth: new DateOnly(2026, 11, 1))!.Message.ShouldContain("November 2026 is excused whole");

        var hours = Change(day, day.AddDays(7), withHours: [day.AddDays(1), day])!;
        hours.Code.ShouldBe(ErrorCodes.TimeOffHoursLogged);
        hours.Message.ShouldContain("3 Nov 2026, 4 Nov 2026");
        var many = Enumerable.Range(0, 7).Select(day.AddDays).ToArray();
        Change(day, day.AddDays(7), withHours: many)!.Message.ShouldContain("and 2 more");
    }

    [Fact]
    public void Approved_time_off_can_be_rejected_until_its_last_day()
    {
        TimeOffRules.CheckReject(TimeOffStatus.Approved, Today, Today).ShouldBeNull();
        TimeOffRules.CheckReject(TimeOffStatus.Approved, Today.AddDays(-1), Today)!.Code.ShouldBe(ErrorCodes.TimeOffOver);
        TimeOffRules.CheckReject(TimeOffStatus.Pending, Today, Today)!.Code.ShouldBe(ErrorCodes.TimeOffNotApproved);
    }

    [Fact]
    public void Rejected_midway_the_days_before_stay_time_off_and_the_rejection_day_is_a_work_day()
    {
        var period = Rejected(5, 13, on: 9);
        period.LastTaken.ShouldBe(new DateOnly(2026, 11, 8));
        period.IsOff(new(2026, 11, 8)).ShouldBeTrue();
        period.IsOff(Today).ShouldBeFalse();

        // rejected before it began: nothing was taken
        Rejected(10, 13, on: 9).Takes(new(2026, 11, 1), new(2026, 11, 30)).ShouldBeFalse();
        new Period(3, new(2026, 11, 10), new(2026, 11, 12), TimeOffStatus.Declined, null, null).IsOff(new(2026, 11, 10)).ShouldBeFalse();
    }

    [Fact]
    public void Hours_logged_for_someone_never_land_on_a_day_off()
    {
        IReadOnlyList<Period> periods = [Approved(9, 13)];
        TimeOffRules.CheckEntryDays([new(2026, 11, 6)], periods).ShouldBeNull();
        TimeOffRules.CheckEntryDays([new(2026, 11, 6), new(2026, 11, 10)], periods)!.Code.ShouldBe(ErrorCodes.TimeOffDay);
    }

    [Fact]
    public void Only_hours_newly_put_on_a_day_off_wait_for_approval()
    {
        IReadOnlyList<Period> periods = [Approved(9, 13)];
        DateOnly Nov(int day) => new(2026, 11, day);
        TimeOffRules.EntryDays New(params int[] days) => new([], days.Select(Nov).ToList());
        TimeOffRules.EntryDays Moved(int from, int to) => new([Nov(from)], [Nov(to)]);

        // off today, hours on any other day go as always, a reopened past month included
        TimeOffRules.NewDayOff([New(6)], periods).ShouldBeNull();
        TimeOffRules.NewDayOff([New(2), New(14)], periods).ShouldBeNull();

        // new hours on a day off, or an entry moved onto one: the first such day
        TimeOffRules.NewDayOff([New(11), New(10)], periods).ShouldBe(Nov(10));
        TimeOffRules.NewDayOff([Moved(6, 12)], periods).ShouldBe(Nov(12));
        TimeOffRules.NewDayOff([New(8, 9)], periods).ShouldBe(Nov(9));

        // hours already there (approved) are changed freely, and so are those moved off it
        TimeOffRules.NewDayOff([Moved(10, 10)], periods).ShouldBeNull();
        TimeOffRules.NewDayOff([Moved(10, 6)], periods).ShouldBeNull();
        TimeOffRules.AnyOff([Nov(13)], periods).ShouldBeTrue();
        TimeOffRules.AnyOff([Nov(14)], periods).ShouldBeFalse();
    }

    [Fact]
    public void Days_off_reach_the_pay_as_day_excuses_month_by_month()
    {
        var excuses = TimeOffRules.ExcusesIn(new(2026, 11, 1), [new Period(4, new(2026, 10, 30), new(2026, 11, 2), TimeOffStatus.Approved, null, false)]);
        var excuse = excuses.ShouldHaveSingleItem();
        excuse.TimeOff.ShouldBeTrue();
        excuse.IsPaid.ShouldBeFalse();
        excuse.Kind.ShouldBe(ExcuseKind.Days);
        excuse.Days.ShouldBe([new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2)]);

        TimeOffRules.ExcusesIn(new(2026, 12, 1), [Approved(9, 13)]).ShouldBeEmpty();
    }

    [Fact]
    public void A_day_of_time_off_is_not_excused_by_hand_too()
    {
        IReadOnlyList<Period> periods = [Approved(9, 13)];
        TimeOffRules.CheckExcuse(ExcuseKind.Days, [new(2026, 11, 10)], periods)!.Code.ShouldBe(ErrorCodes.ExcuseOnTimeOff);
        TimeOffRules.CheckExcuse(ExcuseKind.WholeMonth, [], periods)!.Code.ShouldBe(ErrorCodes.ExcuseOnTimeOff);
        TimeOffRules.CheckExcuse(ExcuseKind.Days, [new(2026, 11, 16)], periods).ShouldBeNull();
        TimeOffRules.CheckExcuse(ExcuseKind.Minutes, [], periods).ShouldBeNull();
        TimeOffRules.CheckExcuse(ExcuseKind.WholeMonth, [], []).ShouldBeNull();
    }
}
