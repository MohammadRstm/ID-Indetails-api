using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PocketRulesTests
{
    [Fact]
    public void The_suggestion_brings_the_site_back_to_a_ten_percent_margin()
    {
        PocketRules.Suggest(agreed: 1000, profit: 50).ShouldBe(50);
        PocketRules.Suggest(agreed: 1000, profit: -300).ShouldBe(400);
        PocketRules.Suggest(agreed: 1000, profit: 100).ShouldBe(0);
        PocketRules.Suggest(agreed: 1000, profit: 450).ShouldBe(0);
    }

    [Fact]
    public void A_contribution_leaves_the_site_above_zero()
    {
        PocketRules.CheckContribution(399.99m, profit: 400).ShouldBeNull();
        PocketRules.CheckContribution(400, profit: 400)!.Code.ShouldBe(ErrorCodes.PocketWouldEmptySite);
        PocketRules.CheckContribution(10, profit: -5)!.Message.ShouldContain("no profit");
    }

    [Fact]
    public void Only_execution_client_sites_move_money_with_the_pocket()
    {
        PocketRules.CheckSite(DomainCodes.Execution, BillingType.Client).ShouldBeNull();
        PocketRules.CheckSite(DomainCodes.Design, BillingType.Client)!.Code.ShouldBe(ErrorCodes.NotForThisDomain);
        PocketRules.CheckSite(DomainCodes.Execution, BillingType.Internal).ShouldNotBeNull();
    }

    [Fact]
    public void A_request_is_decided_once_and_paid_from_what_the_pocket_holds()
    {
        PocketRules.CheckPending(PocketRequestStatus.Pending).ShouldBeNull();
        PocketRules.CheckPending(PocketRequestStatus.Approved)!.Code.ShouldBe(ErrorCodes.PocketRequestDecided);
        PocketRules.CheckPending(PocketRequestStatus.Denied).ShouldNotBeNull();
        PocketRules.CheckBalance(500, balance: 500).ShouldBeNull();
        PocketRules.CheckBalance(500.01m, balance: 500)!.Code.ShouldBe(ErrorCodes.PocketShort);
    }
}
