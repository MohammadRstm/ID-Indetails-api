using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ProfitFloorRulesTests
{
    [Fact]
    public void Only_client_work_with_an_agreed_amount_is_held_to_the_floor()
    {
        ProfitFloorRules.Applies(BillingType.Client, DomainCodes.Design, revenue: 1000, projectFee: null).ShouldBeTrue();
        ProfitFloorRules.Applies(BillingType.Client, DomainCodes.Design, revenue: 0, projectFee: null).ShouldBeFalse();
        ProfitFloorRules.Applies(BillingType.Internal, DomainCodes.Design, revenue: 0, projectFee: null).ShouldBeFalse();
        // an Execution site's agreed amount is its fee; its revenue stays 0 until it is finalised
        ProfitFloorRules.Applies(BillingType.Client, DomainCodes.Execution, revenue: 0, projectFee: 1000).ShouldBeTrue();
        ProfitFloorRules.Applies(BillingType.Client, DomainCodes.Execution, revenue: 0, projectFee: 0).ShouldBeFalse();
    }

    [Fact]
    public void Ten_percent_or_less_is_close_to_zero_and_zero_or_below_is_at_it()
    {
        ProfitFloorRules.StandingOf(profit: 101, agreed: 1000).ShouldBe(ProfitFloorRules.Standing.Fine);
        ProfitFloorRules.StandingOf(profit: 100, agreed: 1000).ShouldBe(ProfitFloorRules.Standing.Close);
        ProfitFloorRules.StandingOf(profit: 0.01m, agreed: 1000).ShouldBe(ProfitFloorRules.Standing.Close);
        ProfitFloorRules.StandingOf(profit: 0, agreed: 1000).ShouldBe(ProfitFloorRules.Standing.AtZero);
        ProfitFloorRules.StandingOf(profit: -40, agreed: 1000).ShouldBe(ProfitFloorRules.Standing.AtZero);
    }

    [Fact]
    public void Alerts_carry_the_margin_as_a_whole_percentage_never_an_amount()
    {
        ProfitFloorRules.MarginPct(profit: 95, agreed: 1000).ShouldBe(10);
        ProfitFloorRules.MarginPct(profit: -40, agreed: 50).ShouldBe(-80);
        ProfitFloorRules.MarginPct(profit: 10, agreed: 0).ShouldBe(0);
    }

    [Fact]
    public void A_blocked_change_is_decided_once()
    {
        ProfitFloorRules.CheckPending(wasPending: true).ShouldBeNull();
        ProfitFloorRules.CheckPending(wasPending: false)!.Code.ShouldBe(ErrorCodes.BlockedChangeDecided);
    }
}
