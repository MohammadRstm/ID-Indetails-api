using ID_Indetails.api.Business.Website;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class WebsiteAnalyticsRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Fact]
    public void Without_dates_the_period_is_the_28_days_ending_yesterday_beside_the_28_before()
    {
        var period = WebsiteAnalyticsRules.Period(null, null, compare: true, Today);

        period.ShouldBe(new AnalyticsPeriod(new DateOnly(2026, 9, 22), new DateOnly(2026, 10, 19), new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 21)));
        period.Days.ShouldBe(28);
    }

    [Fact]
    public void A_period_ends_today_at_the_latest_and_the_previous_one_has_its_length()
    {
        var period = WebsiteAnalyticsRules.Period(new DateOnly(2026, 10, 14), new DateOnly(2026, 12, 31), compare: true, Today);

        period.ShouldBe(new AnalyticsPeriod(new DateOnly(2026, 10, 14), Today, new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 13)));
    }

    [Fact]
    public void A_period_starting_in_the_future_shows_today_and_one_not_compared_has_no_previous()
    {
        WebsiteAnalyticsRules.Period(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5), compare: false, Today)
            .ShouldBe(new AnalyticsPeriod(Today, Today, null, null));
    }

    [Fact]
    public void A_recent_period_is_kept_minutes_and_a_settled_one_hours()
    {
        WebsiteAnalyticsRules.CacheLifetime(Today, Today).ShouldBe(WebsiteAnalyticsRules.RecentCacheLifetime);
        WebsiteAnalyticsRules.CacheLifetime(Today.AddDays(-7), Today).ShouldBe(WebsiteAnalyticsRules.RecentCacheLifetime);
        WebsiteAnalyticsRules.CacheLifetime(Today.AddDays(-8), Today).ShouldBe(WebsiteAnalyticsRules.SettledCacheLifetime);
    }

    [Fact]
    public void The_figures_are_worked_out_from_the_counts_and_nothing_divides_by_zero()
    {
        var totals = WebsiteAnalyticsRules.Totals(new AnalyticsCounts(40, 30, 50, 20, 150, 2400, 900, 6), contactRequests: 3);

        totals.ViewsPerSession.ShouldBe(3);
        totals.EngagementRate.ShouldBe(0.4);
        totals.AverageEngagementSeconds.ShouldBe(60);
        totals.ContactRequests.ShouldBe(3);

        var empty = WebsiteAnalyticsRules.Totals(new AnalyticsCounts(0, 0, 0, 0, 0, 0, 0, 0), 0);
        empty.ViewsPerSession.ShouldBe(0);
        empty.EngagementRate.ShouldBe(0);
        empty.AverageEngagementSeconds.ShouldBe(0);
    }

    [Fact]
    public void Every_day_is_there_and_beside_the_previous_period_s_day_in_the_same_position()
    {
        var period = new AnalyticsPeriod(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3), new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 30));

        var days = WebsiteAnalyticsRules.Days(
            period,
            [new AnalyticsDayCounts(new DateOnly(2026, 10, 2), 5, 6, 9)],
            [new AnalyticsDayCounts(new DateOnly(2026, 9, 28), 1, 1, 2)]);

        days.Select(d => d.Date).ShouldBe([new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 3)]);
        days.Select(d => d.Visitors).ShouldBe([0L, 5L, 0L]);
        days[0].PreviousDate.ShouldBe(new DateOnly(2026, 9, 28));
        days.Select(d => d.PreviousViews).ShouldBe([2L, 0L, 0L]);
    }

    [Fact]
    public void Days_of_a_period_not_compared_have_no_previous_day()
    {
        var day = WebsiteAnalyticsRules.Days(new AnalyticsPeriod(Today, Today, null, null), [], []).Single();

        day.Visitors.ShouldBe(0);
        day.PreviousDate.ShouldBeNull();
        day.PreviousVisitors.ShouldBeNull();
    }

    [Theory]
    [InlineData("/projects/villa-rizk", "villa-rizk")]
    [InlineData("/projects/villa-rizk/", "villa-rizk")]
    [InlineData("/projects/caf%C3%A9", "café")]
    [InlineData("/projects/", null)]
    [InlineData("/projects/a/b", null)]
    [InlineData("/projects", null)]
    [InlineData("/studio", null)]
    public void A_project_page_is_known_by_its_address(string path, string? slug)
        => WebsiteAnalyticsRules.ProjectSlug(path).ShouldBe(slug);
}
