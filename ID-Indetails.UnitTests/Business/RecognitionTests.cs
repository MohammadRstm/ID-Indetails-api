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
        ], [], null);
        byLabour.ShouldBe(new Dictionary<DateOnly, decimal> { [new(2026, 5, 1)] = 750m, [new(2026, 6, 1)] = 250m });

        var byOthers = Recognition.ByMonth(1000m,
        [
            new Cost(CostSource.Materials, new(2026, 5, 3), 100m),
            new Cost(CostSource.GroundLabour, new(2026, 6, 3), 300m),
        ], [], null);
        byOthers[new DateOnly(2026, 6, 1)].ShouldBe(750m);

        Recognition.ByMonth(1000m, [], [], null).ShouldBeEmpty();
        Recognition.ByMonth(0m, [new Cost(CostSource.Labour, new(2026, 5, 3), 300m)], [], null).ShouldBeEmpty();
    }

    [Fact]
    public void Without_a_priced_cost_revenue_follows_the_hours_logged_then_the_completion_month()
    {
        // A priced cost wins over the hours logged.
        Recognition.ByMonth(1000m, [new Cost(CostSource.Labour, new(2026, 5, 3), 300m)], [(new DateOnly(2026, 6, 3), 600)], null)
            .ShouldBe(new Dictionary<DateOnly, decimal> { [new(2026, 5, 1)] = 1000m });

        // Work only by someone with no pay: 3 hours in May, 1 in June.
        Recognition.ByMonth(1000m, [], [(new DateOnly(2026, 5, 3), 120), (new DateOnly(2026, 5, 20), 60), (new DateOnly(2026, 6, 3), 60)], null)
            .ShouldBe(new Dictionary<DateOnly, decimal> { [new(2026, 5, 1)] = 750m, [new(2026, 6, 1)] = 250m });

        // Nothing at all: the month it was completed, or nowhere while it is still running.
        Recognition.ByMonth(1000m, [], [], new DateOnly(2026, 7, 14))
            .ShouldBe(new Dictionary<DateOnly, decimal> { [new(2026, 7, 1)] = 1000m });
        Recognition.ByMonth(1000m, [], [], null).ShouldBeEmpty();
    }
}
