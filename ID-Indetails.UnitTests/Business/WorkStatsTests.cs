using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class WorkStatsTests
{
    private static WorkStats.Worked At(int year, int month, int day, int hour, int minute, int minutes, long projectId = 1)
        => new(0, 1, projectId, new DateTime(year, month, day, hour, minute, 0), minutes);

    [Fact]
    public void Weeks_start_on_the_company_week_start()
    {
        var wednesday = new DateOnly(2026, 9, 30);

        WorkStats.WeekStartOf(wednesday, DayOfWeek.Monday).ShouldBe(new DateOnly(2026, 9, 28));
        WorkStats.WeekStartOf(wednesday, DayOfWeek.Sunday).ShouldBe(new DateOnly(2026, 9, 27));
        WorkStats.WeekStartOf(new DateOnly(2026, 9, 27), DayOfWeek.Monday).ShouldBe(new DateOnly(2026, 9, 21));
        WorkStats.QuarterStartOf(new DateOnly(2026, 8, 15)).ShouldBe(new DateOnly(2026, 7, 1));
        WorkStats.MonthStartOf(new DateOnly(2026, 8, 15)).ShouldBe(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public void Weekly_totals_cover_every_week_oldest_first_even_empty_ones()
    {
        var worked = new[] { At(2026, 9, 29, 9, 0, 60), At(2026, 9, 30, 9, 0, 30), At(2026, 9, 15, 9, 0, 45), At(2026, 8, 1, 9, 0, 999) };

        var weeks = WorkStats.Weekly(worked, new DateOnly(2026, 10, 1), 3, DayOfWeek.Monday);

        weeks.Select(w => w.WeekStart).ShouldBe([new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 28)]);
        weeks.Select(w => w.Minutes).ShouldBe([45, 0, 90]);
        weeks[2].Entries.ShouldBe(2);
    }

    [Fact]
    public void Streaks_count_active_days_and_the_longest_run()
    {
        var worked = new[] { At(2026, 9, 1, 9, 0, 10), At(2026, 9, 2, 9, 0, 10), At(2026, 9, 2, 14, 0, 10), At(2026, 9, 3, 9, 0, 10), At(2026, 9, 7, 9, 0, 10) };

        WorkStats.Streaks(worked, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)).ShouldBe((4, 3));
        WorkStats.Streaks(worked, new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 30)).ShouldBe((2, 1));
        WorkStats.Streaks([], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)).ShouldBe((0, 0));
    }

    [Fact]
    public void Activity_level_follows_the_14_day_weekly_average()
    {
        WorkStats.LevelOf(0).ShouldBe(ActivityLevel.None);
        WorkStats.LevelOf(10).ShouldBe(ActivityLevel.Quiet);
        WorkStats.LevelOf(30 * 60).ShouldBe(ActivityLevel.Steady);
        WorkStats.LevelOf(90 * 60).ShouldBe(ActivityLevel.Busy);
    }

    [Fact]
    public void Hour_heat_spreads_an_entry_over_the_hours_it_covers()
    {
        var cells = WorkStats.HourHeat([At(2026, 9, 28, 9, 30, 90)]);

        cells.ShouldBe([new(DayOfWeek.Monday, 9, 30), new(DayOfWeek.Monday, 10, 60)]);
    }

    [Fact]
    public void Minutes_count_on_the_day_an_entry_starts()
    {
        var worked = new[] { At(2026, 9, 28, 23, 0, 120), At(2026, 9, 29, 9, 0, 60) };

        WorkStats.Minutes(worked, new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 28)).ShouldBe(120);
        WorkStats.Daily(worked).ShouldBe([new(new DateOnly(2026, 9, 28), 120), new(new DateOnly(2026, 9, 29), 60)]);
    }

    [Fact]
    public void Work_past_midnight_is_split_between_the_days()
    {
        WorkStats.SplitByDay(new DateTime(2026, 9, 28, 23, 0, 0), 120)
            .ShouldBe([new(new DateOnly(2026, 9, 28), 60), new(new DateOnly(2026, 9, 29), 60)]);
        WorkStats.SplitByDay(new DateTime(2026, 9, 28, 9, 0, 0), 30).ShouldBe([new(new DateOnly(2026, 9, 28), 30)]);
    }

    [Fact]
    public void Quiet_runs_of_four_days_or_more_are_gaps()
    {
        var worked = new HashSet<DateOnly> { new(2026, 9, 1), new(2026, 9, 6), new(2026, 9, 8) };

        WorkStats.Gaps(worked, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 12))
            .ShouldBe([new(new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 5), 4), new(new DateOnly(2026, 9, 9), new DateOnly(2026, 9, 12), 4)]);
    }
}
