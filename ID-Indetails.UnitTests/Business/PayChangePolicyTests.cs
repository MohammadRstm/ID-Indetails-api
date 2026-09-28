using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PayChangePolicyTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    [Fact]
    public void First_pay_may_apply_from_today_or_the_past()
    {
        PayChangePolicy.Check(Today, Today, null).ShouldBeNull();
        PayChangePolicy.Check(new DateOnly(2026, 1, 1), Today, null).ShouldBeNull();
    }

    [Fact]
    public void Pay_never_applies_from_a_future_date()
        => PayChangePolicy.Check(Today.AddDays(1), Today, null)!.Code.ShouldBe(ErrorCodes.PayDateInFuture);

    [Fact]
    public void A_change_must_apply_after_the_latest_change()
    {
        var latest = new DateOnly(2026, 6, 1);

        // Example from the business rules: a March change after a June raise is refused, not merged.
        var refused = PayChangePolicy.Check(new DateOnly(2026, 3, 1), Today, latest)!;
        refused.Code.ShouldBe(ErrorCodes.PayChangeNotAfterLatest);
        refused.Message.ShouldContain("1 Jun 2026");

        PayChangePolicy.Check(latest, Today, latest)!.Code.ShouldBe(ErrorCodes.PayChangeNotAfterLatest);
        PayChangePolicy.Check(latest.AddDays(1), Today, latest).ShouldBeNull();
    }
}
