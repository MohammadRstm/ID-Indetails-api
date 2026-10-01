using ID_Indetails.api.Business.Finance;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class RecognitionTests
{
    [Fact]
    public void A_period_takes_each_month_by_its_calendar_days()
    {
        var shares = Recognition.Months(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 10));

        shares.Select(s => s.Month).ShouldBe([new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1)]);
        shares[0].Calendar.ShouldBe(1m);
        shares[1].Calendar.ShouldBe(10m / 30m);  // 10 of September's 30 days
    }

    [Fact]
    public void Half_a_month_is_half_its_calendar()
    {
        Recognition.Months(new DateOnly(2026, 6, 16), new DateOnly(2026, 6, 30)).ShouldHaveSingleItem().Calendar.ShouldBe(0.5m);
    }
}
