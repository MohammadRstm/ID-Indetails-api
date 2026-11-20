using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PayChangePolicyTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    [Fact]
    public void Pay_may_apply_from_today_or_any_earlier_day()
    {
        PayChangePolicy.Check(Today, Today, "USD", null).ShouldBeNull();
        PayChangePolicy.Check(new DateOnly(2026, 1, 1), Today, "USD", null).ShouldBeNull();
        // before or on an earlier change: it replaces what follows, so it is allowed
        PayChangePolicy.Check(new DateOnly(2026, 3, 13), Today, "USD", "USD").ShouldBeNull();
    }

    [Fact]
    public void Pay_never_applies_from_a_future_date()
        => PayChangePolicy.Check(Today.AddDays(1), Today, "USD", null)!.Code.ShouldBe(ErrorCodes.PayDateInFuture);

    [Fact]
    public void A_change_of_currency_starts_on_the_first_of_a_month()
    {
        var refused = PayChangePolicy.Check(new DateOnly(2026, 9, 15), Today, "LBP", "USD")!;
        refused.Code.ShouldBe(ErrorCodes.PayCurrencyChangeNotFirst);
        refused.Field.ShouldBe("effectiveFrom");

        PayChangePolicy.Check(new DateOnly(2026, 9, 1), Today, "LBP", "USD").ShouldBeNull();
        // the same currency in any case, and a first salary in any currency, start any day
        PayChangePolicy.Check(new DateOnly(2026, 9, 15), Today, "usd", "USD").ShouldBeNull();
        PayChangePolicy.Check(new DateOnly(2026, 9, 15), Today, "LBP", null).ShouldBeNull();
    }
}
