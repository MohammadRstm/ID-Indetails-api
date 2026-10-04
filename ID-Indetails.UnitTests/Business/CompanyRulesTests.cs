using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Company;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class CompanyRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    [Fact]
    public void Weekdays_round_trip_through_the_mask()
    {
        WorkWeek.ToMask([1, 2, 3, 4, 5]).ShouldBe((byte)62);
        WorkWeek.FromMask(62).ShouldBe([1, 2, 3, 4, 5]);
    }

    [Theory]
    // 9-5 with a paid 1h break requires 7h; a 6h day or shorter has no break taken off.
    [InlineData(540, 1020, 60, 420)]
    [InlineData(540, 900, 60, 360)]
    [InlineData(540, 901, 60, 301)]
    public void The_break_only_lowers_required_hours_on_days_over_6_hours(int start, int end, int breakMinutes, int required)
        => WorkWeek.RequiredMinutes(start, end, breakMinutes).ShouldBe(required);

    [Fact]
    public void Working_time_changes_start_today_or_later_and_only_future_ones_are_replaced()
    {
        WorkWeek.CheckEffectiveFrom(Today.AddDays(-1), Today, false)!.Code.ShouldBe(ErrorCodes.WorkingTimeInPast);
        WorkWeek.CheckEffectiveFrom(Today, Today, false).ShouldBeNull();
        WorkWeek.CheckEffectiveFrom(Today, Today, true)!.Code.ShouldBe(ErrorCodes.WorkingTimeAlreadyChanged);
        WorkWeek.CheckEffectiveFrom(Today.AddDays(3), Today, true).ShouldBeNull();
    }

    [Fact]
    public void Overhead_is_charged_whole_months_at_the_amount_in_force()
    {
        var amounts = new[] { new OverheadRules.Amount(new(2026, 1, 1), 100m), new OverheadRules.Amount(new(2026, 6, 1), 150m) };
        var started = new DateOnly(2026, 1, 1);
        var ended = new DateOnly(2026, 8, 31);

        OverheadRules.AmountFor(new(2025, 12, 1), started, ended, amounts).ShouldBeNull();
        OverheadRules.AmountFor(new(2026, 5, 1), started, ended, amounts).ShouldBe(100m);
        OverheadRules.AmountFor(new(2026, 6, 1), started, ended, amounts).ShouldBe(150m);
        OverheadRules.AmountFor(new(2026, 8, 1), started, ended, amounts).ShouldBe(150m);
        OverheadRules.AmountFor(new(2026, 9, 1), started, ended, amounts).ShouldBeNull();
    }

    [Fact]
    public void A_cancelled_overhead_line_can_t_be_resumed_once_another_carries_it_on()
    {
        var ended = new DateOnly(2026, 9, 30);
        (string, long, DateOnly)[] carriedOn = [("Office rent", 2, new(2026, 10, 1))];

        OverheadRules.CheckResume("office rent", 2, ended, carriedOn)!.Code.ShouldBe(ErrorCodes.OverheadCarriedOn);
        OverheadRules.CheckResume("Office rent", 1, ended, carriedOn).ShouldBeNull();
        OverheadRules.CheckResume("Office rent", 2, ended, [("Office rent", 2, new(2026, 11, 1))]).ShouldBeNull();
        OverheadRules.CheckResume("Office rent", 2, ended, []).ShouldBeNull();
    }

    [Fact]
    public void An_overhead_currency_changes_from_this_month_or_later_and_splits_a_line_already_charged()
    {
        OverheadRules.CheckCurrencyChange("LBP", "USD", null, new(2026, 9, 1), Today).ShouldBeNull();
        OverheadRules.CheckCurrencyChange("LBP", "usd", null, new(2026, 8, 1), Today)!.Code.ShouldBe(ErrorCodes.OverheadMonthInPast);
        OverheadRules.CheckCurrencyChange("USD", "usd", null, new(2026, 9, 1), Today)!.Code.ShouldBe(ErrorCodes.OverheadSameCurrency);
        OverheadRules.CheckCurrencyChange("LBP", "USD", new DateOnly(2026, 9, 30), new(2026, 10, 1), Today)!.Code
            .ShouldBe(ErrorCodes.OverheadAlreadyCancelled);

        OverheadRules.CurrencyChangeSplits(new(2026, 1, 1), new(2026, 10, 1)).ShouldBeTrue();
        OverheadRules.CurrencyChangeSplits(new(2026, 10, 1), new(2026, 10, 1)).ShouldBeFalse();
        OverheadRules.CurrencyChangeSplits(new(2026, 11, 1), new(2026, 10, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Overhead_months_are_never_in_the_past_or_before_a_later_amount()
    {
        OverheadRules.CheckMonth(new(2026, 8, 1), Today, null, "fromMonth")!.Kind.ShouldBe(DenialKind.Validation);
        OverheadRules.CheckMonth(new(2026, 9, 1), Today, null, "fromMonth").ShouldBeNull();
        OverheadRules.CheckMonth(new(2026, 10, 1), Today, new DateOnly(2026, 11, 1), "fromMonth")!.Kind.ShouldBe(DenialKind.BusinessRule);
        OverheadRules.CheckMonth(new(2026, 11, 1), Today, new DateOnly(2026, 11, 1), "fromMonth").ShouldBeNull();
    }

    [Fact]
    public void A_new_overhead_category_goes_after_the_rest_and_before_other()
    {
        OverheadRules.NextSortOrder([10, 20, 130, OverheadRules.LastSortOrder]).ShouldBe(140);
        OverheadRules.NextSortOrder([OverheadRules.LastSortOrder]).ShouldBe(10);
        OverheadRules.NextSortOrder([]).ShouldBe(10);
    }

    [Fact]
    public void A_category_in_use_stays()
    {
        OverheadRules.CheckRemoveGroup(0).ShouldBeNull();
        OverheadRules.CheckRemoveGroup(2)!.Code.ShouldBe(ErrorCodes.OverheadGroupInUse);
    }
}
