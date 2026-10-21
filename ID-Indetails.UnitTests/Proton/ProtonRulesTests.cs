using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonRulesTests
{
    // A Wednesday.
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Theory]
    [InlineData(ProtonPeriod.Today, "2026-10-07", "2026-10-07")]
    [InlineData(ProtonPeriod.Yesterday, "2026-10-06", "2026-10-06")]
    [InlineData(ProtonPeriod.ThisWeek, "2026-10-05", "2026-10-07")]
    [InlineData(ProtonPeriod.LastWeek, "2026-09-28", "2026-10-04")]
    [InlineData(ProtonPeriod.ThisMonth, "2026-10-01", "2026-10-07")]
    [InlineData(ProtonPeriod.LastMonth, "2026-09-01", "2026-09-30")]
    [InlineData(ProtonPeriod.ThisQuarter, "2026-10-01", "2026-10-07")]
    [InlineData(ProtonPeriod.LastQuarter, "2026-07-01", "2026-09-30")]
    [InlineData(ProtonPeriod.ThisYear, "2026-01-01", "2026-10-07")]
    [InlineData(ProtonPeriod.LastYear, "2025-01-01", "2025-12-31")]
    [InlineData(ProtonPeriod.Last7Days, "2026-10-01", "2026-10-07")]
    [InlineData(ProtonPeriod.Last30Days, "2026-09-08", "2026-10-07")]
    public void Period_words_become_company_days_and_running_periods_end_today(ProtonPeriod period, string from, string to)
    {
        var (range, denial) = ProtonPeriods.Resolve(period, null, null, Today, DayOfWeek.Monday);

        denial.ShouldBeNull();
        range.ShouldBe(new DayRange(DateOnly.Parse(from), DateOnly.Parse(to)));
    }

    [Fact]
    public void Weeks_follow_the_companys_first_day()
    {
        ProtonPeriods.Resolve(ProtonPeriod.ThisWeek, null, null, Today, DayOfWeek.Sunday).Range
            .ShouldBe(new DayRange(new DateOnly(2026, 10, 4), Today));
        ProtonPeriods.Resolve(ProtonPeriod.LastWeek, null, null, Today, DayOfWeek.Sunday).Range
            .ShouldBe(new DayRange(new DateOnly(2026, 9, 27), new DateOnly(2026, 10, 3)));
    }

    [Fact]
    public void Last_month_from_the_31st_is_the_whole_previous_month()
    {
        ProtonPeriods.Resolve(ProtonPeriod.LastMonth, null, null, new DateOnly(2026, 3, 31), DayOfWeek.Monday).Range
            .ShouldBe(new DayRange(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
    }

    [Fact]
    public void A_custom_period_needs_both_days_in_order_and_within_the_limit()
    {
        ProtonPeriods.Resolve(ProtonPeriod.Custom, new DateOnly(2026, 1, 1), null, Today, DayOfWeek.Monday).Denial.ShouldNotBeNull();
        ProtonPeriods.Resolve(ProtonPeriod.Custom, new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), Today, DayOfWeek.Monday).Denial.ShouldNotBeNull();
        ProtonPeriods.Resolve(ProtonPeriod.Custom, new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1), Today, DayOfWeek.Monday, maxDays: 366)
            .Denial!.Message.ShouldContain("366");

        ProtonPeriods.Resolve(ProtonPeriod.Custom, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), Today, DayOfWeek.Monday).Range
            .ShouldBe(new DayRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }

    [Theory]
    [InlineData("2026-10-06", ProjectStatus.Active, true)]
    [InlineData("2026-10-06", ProjectStatus.Hold, true)]
    [InlineData("2026-10-07", ProjectStatus.Active, false)]
    [InlineData("2026-10-06", ProjectStatus.Completed, false)]
    [InlineData("2026-10-06", ProjectStatus.Archived, false)]
    public void A_project_is_overdue_once_its_target_date_has_passed_while_active_or_on_hold(string target, ProjectStatus status, bool overdue)
        => ProjectOverdue.IsOverdue(DateOnly.Parse(target), status, Today).ShouldBe(overdue);

    [Fact]
    public void A_project_without_a_target_date_is_never_overdue()
    {
        ProjectOverdue.IsOverdue(null, ProjectStatus.Active, Today).ShouldBeFalse();
        ProjectOverdue.DaysOverdue(null, ProjectStatus.Active, Today).ShouldBeNull();
        ProjectOverdue.DaysOverdue(new DateOnly(2026, 9, 30), ProjectStatus.Active, Today).ShouldBe(7);
    }

    [Fact]
    public void Cost_is_the_tokens_at_their_price_per_million()
    {
        var prices = new ProtonTokenPrices(Input: 2m, Output: 10m, CacheRead: 0.2m, CacheWrite: 2.5m);

        ProtonUsageRules.CostOf(new ProtonTokens(1_000_000, 100_000, 500_000, 200_000), prices)
            .ShouldBe(2m + 1m + 0.1m + 0.5m);
        ProtonUsageRules.CostOf(ProtonTokens.None, prices).ShouldBe(0m);
    }

    [Fact]
    public void A_call_whose_prompt_passes_the_threshold_is_priced_on_the_long_prompt_card()
    {
        var prices = new ProtonTokenPrices(Input: 0.10m, Output: 0.50m, CacheRead: 0.01m, CacheWrite: 0.125m)
        {
            LongPromptAbove = 100_000,
            LongPrompt = new ProtonTokenPrices(Input: 0.50m, Output: 2.50m, CacheRead: 0.05m, CacheWrite: 0.625m),
        };

        // the prompt counts cached tokens too: 20,000 + 80,000 is exactly the threshold, still the standard card
        ProtonUsageRules.CostOf(new ProtonTokens(20_000, 1_000_000, 80_000, 0), prices)
            .ShouldBe(0.002m + 0.5m + 0.0008m);
        // past it is the long-prompt card, for the output as well
        ProtonUsageRules.CostOf(new ProtonTokens(20_002, 1_000_000, 80_000, 0), prices)
            .ShouldBe(0.010001m + 2.5m + 0.004m);
    }

    [Fact]
    public void An_allowance_is_used_up_at_its_limit_and_never_shows_negative_remaining()
    {
        var budget = ProtonUsageRules.Budget(monthlyBudget: 50m, topUps: 10m, spent: 45m);
        budget.Limit.ShouldBe(60m);
        budget.Remaining.ShouldBe(15m);
        budget.UsedPct.ShouldBe(75);
        budget.IsExhausted.ShouldBeFalse();

        var over = ProtonUsageRules.Budget(50m, 0m, 51m);
        over.IsExhausted.ShouldBeTrue();
        over.Remaining.ShouldBe(0m);
        over.UsedPct.ShouldBe(100);

        ProtonUsageRules.Questions(perUser: 3, asked: 3).IsExhausted.ShouldBeTrue();
        ProtonUsageRules.Budget(0m, 0m, 0m).IsExhausted.ShouldBeTrue();
    }
}
