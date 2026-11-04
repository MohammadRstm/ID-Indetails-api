using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using Line = ID_Indetails.api.Business.Projects.DesignVoRules.Line;

namespace ID_Indetails.UnitTests.Business;

public class DesignVoRulesTests
{
    [Fact]
    public void A_variation_order_has_described_lines_to_the_cent_that_change_the_price()
    {
        DesignVoRules.CheckLines([new Line("Guest house", 9000m), new Line("No pergola", -1500m)]).ShouldBeNull();
        DesignVoRules.Total([new Line("Guest house", 9000m), new Line("No pergola", -1500m)]).ShouldBe(7500m);

        DesignVoRules.CheckLines([])!.Field.ShouldBe("lines");
        DesignVoRules.CheckLines([new Line(" ", 100m)])!.Message.ShouldBe("Describe each line.");
        DesignVoRules.CheckLines([new Line("Guest house", 0m)])!.Message.ShouldContain("to the cent");
        DesignVoRules.CheckLines([new Line("Guest house", 10.005m)])!.Message.ShouldContain("to the cent");
        DesignVoRules.CheckLines([new Line("More", 500m), new Line("Less", -500m)])!.Message.ShouldContain("add up to nothing");
    }

    [Fact]
    public void Amounts_given_by_hand_go_to_the_projects_departments_and_add_up_to_the_total()
    {
        DesignVoRules.CheckAllocations(DesignVoSplit.Shares, [1, 2], [], 1000m).ShouldBeNull();
        DesignVoRules.CheckAllocations(DesignVoSplit.Shares, [1, 2], [(1, 1000m)], 1000m)!.Field.ShouldBe("allocations");

        DesignVoRules.CheckAllocations(DesignVoSplit.Departments, [1, 2], [(1, 700m), (2, 300m)], 1000m).ShouldBeNull();
        DesignVoRules.CheckAllocations(DesignVoSplit.Departments, [1, 2], [(2, 1000m)], 1000m).ShouldBeNull();
        DesignVoRules.CheckAllocations(DesignVoSplit.Departments, [], [(1, 1000m)], 1000m)!.Field.ShouldBe("split");
        DesignVoRules.CheckAllocations(DesignVoSplit.Departments, [1, 2], [(3, 1000m)], 1000m)!.Field.ShouldBe("allocations");
        DesignVoRules.CheckAllocations(DesignVoSplit.Departments, [1, 2], [(1, 500m), (1, 500m)], 1000m)!.Field.ShouldBe("allocations");
        DesignVoRules.CheckAllocations(DesignVoSplit.Departments, [1, 2], [(1, 700m), (2, 200m)], 1000m)!.Message.ShouldContain("add up to");
    }

    [Fact]
    public void By_the_shares_the_total_is_divided_to_the_cent_and_adds_up_exactly()
    {
        DesignVoRules.ByShares(1000m, [(1, 0.3334m), (2, 0.3333m), (3, 0.3333m)])
            .ShouldBe([(1L, 333.40m), (2L, 333.30m), (3L, 333.30m)]);

        // what rounding leaves goes to the biggest share
        var odd = DesignVoRules.ByShares(100.01m, [(1, 0.5m), (2, 0.5m)]);
        odd.Sum(a => a.Amount).ShouldBe(100.01m);
        odd.ShouldBe([(1L, 50.00m), (2L, 50.01m)]);

        DesignVoRules.ByShares(-300m, [(1, 0.6m), (2, 0.4m)]).ShouldBe([(1L, -180m), (2L, -120m)]);
        DesignVoRules.ByShares(1000m, []).ShouldBeEmpty();
    }

    [Fact]
    public void Only_a_pending_one_is_confirmed_and_none_takes_the_price_below_zero()
    {
        DesignVoRules.CheckPending(DesignVoStatus.Pending).ShouldBeNull();
        DesignVoRules.CheckPending(DesignVoStatus.Applied)!.Code.ShouldBe(ErrorCodes.DesignVoNotPending);
        DesignVoRules.CheckPending(DesignVoStatus.Cancelled)!.Message.ShouldContain("cancelled");

        DesignVoRules.CheckPrice(10000m, -10000m).ShouldBeNull();
        DesignVoRules.CheckPrice(10000m, -10000.01m)!.Code.ShouldBe(ErrorCodes.DesignVoBelowZero);
        DesignVoRules.Number(2026, 7).ShouldBe("VO-2026-007");
    }
}
