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
    public void No_hours_land_on_a_day_off_and_nobody_changes_hours_while_off()
    {
        IReadOnlyList<Period> periods = [Approved(9, 13)];
        TimeOffRules.CheckEntryDays([new(2026, 11, 6)], periods).ShouldBeNull();
        TimeOffRules.CheckEntryDays([new(2026, 11, 6), new(2026, 11, 10)], periods)!.Code.ShouldBe(ErrorCodes.TimeOffDay);
        TimeOffRules.CheckNotOff(Today, periods)!.Code.ShouldBe(ErrorCodes.OnTimeOff);
        TimeOffRules.CheckNotOff(new(2026, 11, 14), periods).ShouldBeNull();
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
