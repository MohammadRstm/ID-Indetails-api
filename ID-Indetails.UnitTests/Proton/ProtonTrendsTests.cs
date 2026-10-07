using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonTrendsTests
{
    // A Wednesday.
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static readonly ProtonThresholds Thresholds = new(
        NotablePct: 30, NotableHours: 10, NotableUsd: 1_000, UnusualHigh: 1.5m, UnusualLow: 0.5m, BaselinePeriods: 4,
        TargetSoonDays: 14, IdleDays: 7, CloseoutProgress: 90);

    private static DayRange Days(string from, string to) => new(DateOnly.Parse(from), DateOnly.Parse(to));

    private static ProtonPeriodPair Compare(ProtonPeriod period, DateOnly today, ProtonComparison comparison = ProtonComparison.Previous)
    {
        var current = ProtonPeriods.Resolve(period, null, null, today, DayOfWeek.Monday).Range!;
        return ProtonPeriods.Compare(period, current, comparison, null, null).Pair!;
    }

    [Theory]
    [InlineData(ProtonPeriod.ThisWeek, "2026-09-28", "2026-09-30", true)]
    [InlineData(ProtonPeriod.ThisMonth, "2026-09-01", "2026-09-07", true)]
    [InlineData(ProtonPeriod.ThisQuarter, "2026-07-01", "2026-07-07", true)]
    [InlineData(ProtonPeriod.ThisYear, "2025-01-01", "2025-10-07", true)]
    [InlineData(ProtonPeriod.LastWeek, "2026-09-21", "2026-09-27", false)]
    [InlineData(ProtonPeriod.LastMonth, "2026-08-01", "2026-08-31", false)]
    [InlineData(ProtonPeriod.LastQuarter, "2026-04-01", "2026-06-30", false)]
    [InlineData(ProtonPeriod.LastYear, "2024-01-01", "2024-12-31", false)]
    [InlineData(ProtonPeriod.Today, "2026-10-06", "2026-10-06", false)]
    [InlineData(ProtonPeriod.Last30Days, "2026-08-09", "2026-09-07", false)]
    public void A_period_is_compared_like_for_like(ProtonPeriod period, string from, string to, bool partial)
    {
        var pair = Compare(period, Today);

        pair.Previous.ShouldBe(Days(from, to));
        pair.Partial.ShouldBe(partial);
    }

    [Fact]
    public void A_running_month_never_reaches_past_the_end_of_the_shorter_month_before()
    {
        // 1–30 March against February: all of February, not into March
        Compare(ProtonPeriod.ThisMonth, new DateOnly(2026, 3, 30)).Previous.ShouldBe(Days("2026-02-01", "2026-02-28"));
    }

    [Fact]
    public void A_whole_month_on_its_last_day_is_not_partial_and_compares_with_the_whole_month_before()
    {
        var pair = Compare(ProtonPeriod.ThisMonth, new DateOnly(2026, 3, 31));

        pair.Partial.ShouldBeFalse();
        pair.Previous.ShouldBe(Days("2026-02-01", "2026-02-28"));
        pair.Note.ShouldBe("the whole month before");
    }

    [Fact]
    public void The_same_period_last_year_lands_29_february_on_the_28th()
    {
        var current = Days("2028-02-01", "2028-02-29");
        ProtonPeriods.Compare(ProtonPeriod.Custom, current, ProtonComparison.SamePeriodLastYear, null, null).Pair!.Previous
            .ShouldBe(Days("2027-02-01", "2027-02-28"));
    }

    [Fact]
    public void Custom_days_are_compared_with_as_many_days_just_before_or_with_the_days_asked_for()
    {
        var current = Days("2026-09-10", "2026-09-19");
        ProtonPeriods.Compare(ProtonPeriod.Custom, current, ProtonComparison.Previous, null, null).Pair!.Previous
            .ShouldBe(Days("2026-08-31", "2026-09-09"));
        ProtonPeriods.Compare(ProtonPeriod.Custom, current, ProtonComparison.Custom, new DateOnly(2025, 9, 10), new DateOnly(2025, 9, 19)).Pair!.Previous
            .ShouldBe(Days("2025-09-10", "2025-09-19"));
        ProtonPeriods.Compare(ProtonPeriod.Custom, current, ProtonComparison.Custom, null, null).Denial.ShouldNotBeNull();
        ProtonPeriods.Compare(ProtonPeriod.Custom, current, ProtonComparison.None, null, null).Pair!.Previous.ShouldBeNull();
    }

    [Theory]
    [InlineData(147, 110, 37, 34)]
    [InlineData(80, 100, -20, -20)]
    [InlineData(0, 0, 0, null)]
    [InlineData(12, 0, 12, null)]
    [InlineData(10, -20, 30, 150)]
    [InlineData(1, 3, -2, -67)]
    public void A_change_is_the_difference_and_its_percentage_of_before(double current, double previous, double diff, int? pct)
    {
        var change = ProtonTrends.Change((decimal)current, (decimal)previous);

        change.Diff.ShouldBe((decimal)diff);
        change.Pct.ShouldBe(pct);
    }

    [Fact]
    public void Contributors_are_the_biggest_movers_with_their_share_of_the_change()
    {
        var contributors = ProtonTrends.Contributors(new[] { ("Alpha", 60m, 20m), ("Beta", 10m, 20m), ("Gamma", 5m, 5m), ("Delta", 0m, 0m) });

        contributors.Select(c => c.Key).ShouldBe(["Alpha", "Beta"]);
        contributors[0].SharePct.ShouldBe(133);
        contributors[1].SharePct.ShouldBe(-33);
        ProtonTrends.Contributors(new[] { ("A", 10m, 0m), ("B", 0m, 10m) }).All(c => c.SharePct is null).ShouldBeTrue();
    }

    [Fact]
    public void A_baseline_needs_every_earlier_period_to_have_something()
    {
        ProtonTrends.Baseline([10, 20, 30, 40], 4).ShouldBe(25);
        ProtonTrends.Baseline([99, 10, 20, 30, 40], 4).ShouldBe(25);
        ProtonTrends.Baseline([10, 20, 40], 4).ShouldBeNull();
        ProtonTrends.Baseline([10, 0, 30, 40], 4).ShouldBeNull();
    }

    [Fact]
    public void Shares_are_whole_percentages_and_zero_of_nothing()
    {
        ProtonTrends.SharePct(1, 3).ShouldBe(33);
        ProtonTrends.SharePct(5, 0).ShouldBe(0);
    }

    [Theory]
    [InlineData(130, 100, true)]   // +30 %, +30 h
    [InlineData(129, 100, false)]  // +29 %
    [InlineData(13, 10, false)]    // +30 % but only 3 h
    [InlineData(10, 0, true)]      // from nothing: the floor decides
    [InlineData(9, 0, false)]
    [InlineData(60, 100, true)]    // falls count too
    public void A_change_is_notable_only_past_both_the_percentage_and_the_floor(double current, double previous, bool notable)
        => ProtonSignals.IsNotable(ProtonTrends.Change((decimal)current, (decimal)previous), Thresholds.NotableHours, Thresholds).ShouldBe(notable);

    [Theory]
    [InlineData(30, 20, true)]
    [InlineData(29, 20, false)]
    [InlineData(10, 20, true)]
    [InlineData(11, 20, false)]
    [InlineData(6, 4, false)]      // 1.5× but only 2 h from it
    public void A_period_is_unusual_only_far_from_its_baseline(double current, double baseline, bool unusual)
        => ProtonSignals.IsUnusual((decimal)current, (decimal)baseline, Thresholds.NotableHours, Thresholds).ShouldBe(unusual);

    [Fact]
    public void Nothing_is_unusual_without_a_baseline()
        => ProtonSignals.IsUnusual(500, null, 10, Thresholds).ShouldBeFalse();

    [Fact]
    public void A_target_is_soon_within_the_days_of_an_active_project_only()
    {
        ProtonSignals.IsTargetSoon(Today.AddDays(14), ProjectStatus.Active, Today, Thresholds).ShouldBeTrue();
        ProtonSignals.IsTargetSoon(Today, ProjectStatus.Active, Today, Thresholds).ShouldBeTrue();
        ProtonSignals.IsTargetSoon(Today.AddDays(15), ProjectStatus.Active, Today, Thresholds).ShouldBeFalse();
        ProtonSignals.IsTargetSoon(Today.AddDays(-1), ProjectStatus.Active, Today, Thresholds).ShouldBeFalse();
        ProtonSignals.IsTargetSoon(Today.AddDays(3), ProjectStatus.Hold, Today, Thresholds).ShouldBeFalse();
        ProtonSignals.IsTargetSoon(null, ProjectStatus.Active, Today, Thresholds).ShouldBeFalse();
    }

    [Fact]
    public void Idle_and_closeout_reuse_the_design_report_rules()
    {
        ProtonSignals.IsIdle(7, Thresholds).ShouldBeTrue();
        ProtonSignals.IsIdle(6, Thresholds).ShouldBeFalse();
        ProtonSignals.IsCloseout(ProjectStatus.Active, 90, Thresholds).ShouldBeTrue();
        ProtonSignals.IsCloseout(ProjectStatus.Hold, 95, Thresholds).ShouldBeFalse();
    }

    [Fact]
    public void Signals_are_grouped_per_project_and_ranked_by_their_most_pressing_one()
    {
        ProtonSignal Signal(ProtonSignalKind kind, long? project, decimal weight = 0, string? domain = "design")
            => new(kind, project, project is { } p ? $"P{p}" : null, domain, weight, "fact", "rule");

        var (groups, more) = ProtonSignals.Rank(
        [
            Signal(ProtonSignalKind.Idle, 1, 20),
            Signal(ProtonSignalKind.ProjectOverdue, 1, 3),
            Signal(ProtonSignalKind.Idle, 2, 30),
            Signal(ProtonSignalKind.OverdueInvoice, 3, 500),
            Signal(ProtonSignalKind.Idle, 4, 9),
            Signal(ProtonSignalKind.NotLogging, null, 2),
            Signal(ProtonSignalKind.NotLogging, null, 1, "execution"),
        ], limit: 4);

        groups.Select(g => g.ProjectId).ShouldBe([3, 1, 2, 4]);
        groups[1].Signals.Select(s => s.Kind).ShouldBe([ProtonSignalKind.ProjectOverdue, ProtonSignalKind.Idle]);
        more.ShouldBe(2);
    }
}
