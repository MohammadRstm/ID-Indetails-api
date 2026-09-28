using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Enums;
using Shouldly;
using Cost = ID_Indetails.api.Business.Finance.Recognition.DatedCost;

namespace ID_Indetails.UnitTests.Business;

public class RecognitionTests
{
    [Fact]
    public void A_closed_month_counts_in_full_and_the_current_month_only_up_to_today()
    {
        var today = new DateOnly(2026, 9, 10);

        var shares = Recognition.Months(new DateOnly(2026, 8, 1), today, today);

        shares.Select(s => s.Month).ShouldBe([new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1)]);
        shares[0].Accrued.ShouldBe(1m);
        shares[0].Calendar.ShouldBe(1m);
        shares[1].Accrued.ShouldBe(1m);          // all 10 elapsed days are in the period
        shares[1].Calendar.ShouldBe(10m / 30m);  // but only 10 of September's 30 days
    }

    [Fact]
    public void Half_a_closed_month_takes_half_its_revenue()
    {
        var shares = Recognition.Months(new DateOnly(2026, 6, 16), new DateOnly(2026, 6, 30), new DateOnly(2026, 9, 1));

        shares.ShouldHaveSingleItem().Accrued.ShouldBe(0.5m);
    }

    [Fact]
    public void Revenue_is_spread_by_labour_when_there_is_any_otherwise_by_other_costs()
    {
        var byLabour = Recognition.ByMonth(1000m,
        [
            new Cost(CostSource.Labour, new(2026, 5, 3), 300m),
            new Cost(CostSource.Labour, new(2026, 6, 3), 100m),
            new Cost(CostSource.Materials, new(2026, 7, 3), 5000m),
        ]);
        byLabour.ShouldBe(new Dictionary<DateOnly, decimal> { [new(2026, 5, 1)] = 750m, [new(2026, 6, 1)] = 250m });

        var byOthers = Recognition.ByMonth(1000m,
        [
            new Cost(CostSource.Materials, new(2026, 5, 3), 100m),
            new Cost(CostSource.GroundLabour, new(2026, 6, 3), 300m),
        ]);
        byOthers[new DateOnly(2026, 6, 1)].ShouldBe(750m);

        Recognition.ByMonth(1000m, []).ShouldBeEmpty();
        Recognition.ByMonth(0m, [new Cost(CostSource.Labour, new(2026, 5, 3), 300m)]).ShouldBeEmpty();
    }
}
