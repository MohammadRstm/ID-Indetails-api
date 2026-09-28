using ID_Indetails.api.Business.Billing;
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
}
