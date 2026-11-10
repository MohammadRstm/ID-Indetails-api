using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.Validators.Billing;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ExchangeRateRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    [Fact]
    public void A_rate_applies_from_today_or_earlier_after_the_latest_one()
    {
        ExchangeRateRules.Check(Today, Today, null).ShouldBeNull();
        ExchangeRateRules.Check(Today.AddDays(-10), Today, Today.AddDays(-11)).ShouldBeNull();
        ExchangeRateRules.Check(Today.AddDays(1), Today, null)!.Code.ShouldBe(ErrorCodes.RateDateInFuture);
        ExchangeRateRules.Check(Today.AddDays(-5), Today, Today.AddDays(-5))!.Code.ShouldBe(ErrorCodes.RateNotAfterLatest);
    }

    [Fact]
    public void Only_other_currencies_than_usd_get_a_positive_rate()
    {
        var validator = new AddExchangeRateRequestValidator();

        validator.Validate(new AddExchangeRateRequest { Currency = "lbp", RatePerUsd = 89_500m }).IsValid.ShouldBeTrue();
        validator.Validate(new AddExchangeRateRequest { Currency = "USD", RatePerUsd = 1m }).IsValid.ShouldBeFalse();
        validator.Validate(new AddExchangeRateRequest { Currency = "EUR", RatePerUsd = 0.9m }).IsValid.ShouldBeFalse();
        validator.Validate(new AddExchangeRateRequest { Currency = "LBP", RatePerUsd = 0m }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void The_feeds_rate_is_added_only_within_ten_percent_of_the_last_one()
    {
        // the first rate of a currency has nothing to compare with
        ExchangeRateRules.IsFeedRateUsable(89_500m, null, 0.10m).ShouldBeTrue();
        ExchangeRateRules.IsFeedRateUsable(1_507.5m, null, 0.10m).ShouldBeTrue();

        // 10% of 89,500 is 8,950: exactly that far up or down is fine, 10.01% (8,958.95) is not
        ExchangeRateRules.IsFeedRateUsable(98_450m, 89_500m, 0.10m).ShouldBeTrue();
        ExchangeRateRules.IsFeedRateUsable(80_550m, 89_500m, 0.10m).ShouldBeTrue();
        ExchangeRateRules.IsFeedRateUsable(98_458.95m, 89_500m, 0.10m).ShouldBeFalse();
        ExchangeRateRules.IsFeedRateUsable(80_541.05m, 89_500m, 0.10m).ShouldBeFalse();

        // a glitch in the source: nothing, or the old 1,507.5 peg
        ExchangeRateRules.IsFeedRateUsable(0m, 89_500m, 0.10m).ShouldBeFalse();
        ExchangeRateRules.IsFeedRateUsable(0m, null, 0.10m).ShouldBeFalse();
        ExchangeRateRules.IsFeedRateUsable(1_507.5m, 89_500m, 0.10m).ShouldBeFalse();
    }

    [Fact]
    public void A_cost_in_another_currency_needs_a_rate_in_force_on_its_day()
    {
        var rates = new ExchangeRateTable([new ExchangeRateTable.Rate("LBP", Today, 89_500m)]);

        // USD needs no rate, on any day
        ExchangeRateRules.CheckConvertible([("USD", new DateOnly(2001, 1, 2))], rates).ShouldBeNull();
        ExchangeRateRules.CheckConvertible([("USD", Today)], new ExchangeRateTable([])).ShouldBeNull();

        // a rate is in force from its day on, until a newer one
        ExchangeRateRules.CheckConvertible([("LBP", Today), ("LBP", Today.AddDays(30))], rates).ShouldBeNull();

        var before = ExchangeRateRules.CheckConvertible([("LBP", Today), ("LBP", Today.AddDays(-1))], rates).ShouldNotBeNull();
        before.Code.ShouldBe(ErrorCodes.ExchangeRateMissing);
        before.Kind.ShouldBe(DenialKind.BusinessRule);
        before.Message.ShouldContain("There is no LBP exchange rate in force on 2026-09-27");
        ExchangeRateRules.CheckConvertible([("EUR", Today)], rates)!.Code.ShouldBe(ErrorCodes.ExchangeRateMissing);
    }
}
