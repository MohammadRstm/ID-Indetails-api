using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Labour;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class LabourRulesTests
{
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private static readonly DateOnly Thursday = new(2026, 9, 24);

    [Fact]
    public void A_site_week_runs_monday_to_saturday()
    {
        LabourRules.WeekStartOf(Thursday).ShouldBe(Monday);
        LabourRules.WeekStartOf(Monday).ShouldBe(Monday);
        LabourRules.WeekStartOf(new DateOnly(2026, 9, 27)).ShouldBe(Monday);
        LabourRules.WorkDays(Monday).ShouldBe([Monday, Monday.AddDays(1), Monday.AddDays(2), Monday.AddDays(3), Monday.AddDays(4), Monday.AddDays(5)]);
    }

    [Fact]
    public void Weeks_run_from_the_project_s_first_week_to_the_current_one_newest_first()
    {
        LabourRules.ReportableWeeks(projectStart: new DateOnly(2026, 9, 9), lastDay: Thursday)
            .ShouldBe([Monday, new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 7)]);

        LabourRules.CheckWeek(Monday, projectStart: new DateOnly(2026, 9, 9), today: Thursday).ShouldBeNull();
        LabourRules.CheckWeek(new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 9), Thursday)!.Code.ShouldBe(ErrorCodes.LabourWeekOutOfRange);
        LabourRules.CheckWeek(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 9), Thursday)!.Code.ShouldBe(ErrorCodes.LabourWeekOutOfRange);
        LabourRules.CheckWeek(Thursday, new DateOnly(2026, 9, 9), Thursday)!.Kind.ShouldBe(DenialKind.Validation);
    }

    [Fact]
    public void Days_belong_to_the_week_are_never_sundays_repeats_or_after_today()
    {
        LabourRules.CheckDays([new(Monday, 10, 40), new(Thursday, 12, 40)], Monday, today: Thursday).ShouldBeNull();
        LabourRules.CheckDays([new(Monday.AddDays(6), 1, 40)], Monday, Thursday)!.Code.ShouldBe(ErrorCodes.LabourDayOutsideWeek);
        LabourRules.CheckDays([new(Monday.AddDays(7), 1, 40)], Monday, Thursday)!.Code.ShouldBe(ErrorCodes.LabourDayOutsideWeek);
        LabourRules.CheckDays([new(Monday, 1, 40), new(Monday, 2, 40)], Monday, Thursday)!.Code.ShouldBe(ErrorCodes.LabourDayOutsideWeek);
        LabourRules.CheckDays([new(Thursday.AddDays(1), 1, 40)], Monday, Thursday)!.Code.ShouldBe(ErrorCodes.LabourDayInFuture);
    }

    [Fact]
    public void A_day_costs_workers_times_daily_pay()
    {
        new LabourRules.Day(Monday, 12, 45.50m).Cost.ShouldBe(546m);
    }

    [Fact]
    public void Defaults_in_force_are_the_latest_dated_on_or_before_the_day_and_the_first_before_any()
    {
        LabourRules.Defaults first = new(new DateOnly(2026, 9, 1), 8, 42), second = new(new DateOnly(2026, 9, 23), 11, 45);

        LabourRules.DefaultsAt([second, first], new DateOnly(2026, 9, 22)).ShouldBe(first);
        LabourRules.DefaultsAt([second, first], new DateOnly(2026, 9, 23)).ShouldBe(second);
        LabourRules.DefaultsAt([second, first], new DateOnly(2026, 8, 1)).ShouldBe(first);
        LabourRules.DefaultsAt([], Monday).ShouldBeNull();
    }

    [Fact]
    public void An_unreported_week_is_filled_in_from_each_day_s_defaults_up_to_today()
    {
        var days = LabourRules.Prefill(Monday, today: Thursday, [new(new DateOnly(2026, 9, 1), 8, 42), new(new DateOnly(2026, 9, 23), 11, 45)]);

        days.ShouldBe([
            new(Monday, 8, 42), new(Monday.AddDays(1), 8, 42),
            new(Monday.AddDays(2), 11, 45), new(Thursday, 11, 45),
        ]);
        LabourRules.Prefill(Monday, Thursday, []).ShouldAllBe(d => d.Workers == 0 && d.DailyPay == 0);
    }

    [Fact]
    public void The_projection_says_when_cash_profit_runs_out_at_today_s_crew()
    {
        LabourRules.Defaults crew = new(Monday, Workers: 10, DailyPay: 40);

        var projection = LabourRules.ProjectZero(done: false, cashProfit: 1000, expectedProfit: 2200, crew, today: Thursday);

        projection.State.ShouldBe(LabourRules.ProjectionState.Ok);
        projection.DailyCost.ShouldBe(400m);
        projection.WorkingDays.ShouldBe(3);
        projection.ZeroOn.ShouldBe(new DateOnly(2026, 9, 28)); // Fri, Sat, then Monday: Sunday is skipped
        projection.WorkingDaysOncePaid.ShouldBe(6);
        projection.ZeroOnOncePaid.ShouldBe(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void The_projection_stops_when_there_is_nothing_to_project()
    {
        LabourRules.Defaults crew = new(Monday, 10, 40);

        LabourRules.ProjectZero(done: true, 1000, 2000, crew, Thursday).State.ShouldBe(LabourRules.ProjectionState.Done);
        LabourRules.ProjectZero(done: false, -5, 2000, crew, Thursday).State.ShouldBe(LabourRules.ProjectionState.Losing);
        LabourRules.ProjectZero(done: false, 1000, 2000, null, Thursday).State.ShouldBe(LabourRules.ProjectionState.NoDefaults);
        LabourRules.ProjectZero(done: false, 1000, 2000, new(Monday, 0, 40), Thursday).State.ShouldBe(LabourRules.ProjectionState.NoDefaults);
        LabourRules.ProjectZero(done: false, 1000, -10, crew, Thursday).ZeroOnOncePaid.ShouldBeNull();
    }

    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(26)]
    [InlineData(27)]
    public void Working_days_are_counted_monday_to_saturday_from_any_day(int september)
    {
        var today = new DateOnly(2026, 9, september);
        for (var days = 1; days <= 20; days++)
        {
            var zeroOn = LabourRules.ProjectZero(false, days * 100m, 0, new(Monday, 1, 100), today).ZeroOn;

            var expected = today;
            for (var counted = 0; counted < days;)
            {
                expected = expected.AddDays(1);
                if (expected.DayOfWeek != DayOfWeek.Sunday)
                {
                    counted++;
                }
            }

            zeroOn.ShouldBe(expected, $"{days} working days from {today}");
        }
    }
}
