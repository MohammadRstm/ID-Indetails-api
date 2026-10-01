using ID_Indetails.api.Business.Finance;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ExpectedRevenueTests
{
    [Fact]
    public void A_payment_counts_when_agreed_and_a_change_by_its_difference_on_its_day()
    {
        ExpectedRevenue.Agreed[] agreed =
        [
            ExpectedRevenue.Change(new(2026, 1, 10), 10_000m, null),
            ExpectedRevenue.Change(new(2026, 3, 5), 12_000m, 10_000m),
            ExpectedRevenue.Change(new(2026, 4, 2), 9_000m, 12_000m),
        ];

        ExpectedRevenue.In(agreed, new(2026, 1, 1), new(2026, 1, 31)).ShouldBe(10_000m);
        ExpectedRevenue.In(agreed, new(2026, 3, 1), new(2026, 3, 31)).ShouldBe(2_000m);
        ExpectedRevenue.In(agreed, new(2026, 4, 1), new(2026, 4, 30)).ShouldBe(-3_000m);
        ExpectedRevenue.In(agreed, new(2026, 1, 1), new(2026, 12, 31)).ShouldBe(9_000m);
    }
}
