using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class BoxRulesTests
{
    [Fact]
    public void Payments_go_in_while_the_project_is_in_the_box_or_waiting_for_a_decision()
    {
        BoxRules.TakesPayments(BoxState.In).ShouldBeTrue();
        BoxRules.TakesPayments(BoxState.Pending).ShouldBeTrue();
        BoxRules.TakesPayments(BoxState.Removed).ShouldBeFalse();
        BoxRules.TakesPayments(null).ShouldBeFalse();
    }

    [Fact]
    public void A_closing_project_waits_for_a_decision_unless_someone_who_sees_money_removes_or_keeps_it()
    {
        BoxRules.OnClose(null, seesMoney: true).ShouldBe((BoxState.Pending, null));
        BoxRules.OnClose(null, seesMoney: false).ShouldBe((BoxState.Pending, null));
        BoxRules.OnClose(BoxChoice.Remove, seesMoney: true).ShouldBe((BoxState.Removed, null));
        BoxRules.OnClose(BoxChoice.Keep, seesMoney: true).ShouldBe((BoxState.In, null));
        BoxRules.OnClose(BoxChoice.Remove, seesMoney: false).Denial!.Kind.ShouldBe(DenialKind.Forbidden);
        BoxRules.OnClose(BoxChoice.Readd, seesMoney: true).Denial!.Field.ShouldBe("box");
    }

    [Fact]
    public void Only_a_removed_project_puts_its_earlier_revenue_back_and_only_when_someone_who_sees_money_says_so()
    {
        BoxRules.OnReopen(BoxState.Removed, BoxChoice.Readd, seesMoney: true).ShouldBe((true, null));
        BoxRules.OnReopen(BoxState.Removed, BoxChoice.Fresh, seesMoney: true).ShouldBe((false, null));
        BoxRules.OnReopen(BoxState.Removed, null, seesMoney: false).ShouldBe((false, null));
        BoxRules.OnReopen(BoxState.In, BoxChoice.Readd, seesMoney: true).ShouldBe((false, null));
        BoxRules.OnReopen(BoxState.Removed, BoxChoice.Readd, seesMoney: false).Denial!.Kind.ShouldBe(DenialKind.Forbidden);
        BoxRules.OnReopen(BoxState.Removed, BoxChoice.Keep, seesMoney: true).Denial!.Field.ShouldBe("box");
    }

    [Fact]
    public void A_removal_takes_what_the_project_put_in_as_far_as_the_box_holds_it()
    {
        BoxRules.Removal(contributed: 1500, balance: 4000).ShouldBe(-1500);
        BoxRules.Removal(contributed: 1500, balance: 600).ShouldBe(-600);
        BoxRules.Removal(contributed: 1500, balance: -50).ShouldBe(0);
        BoxRules.Removal(contributed: 0, balance: 4000).ShouldBe(0);
    }

    [Fact]
    public void Money_goes_only_to_a_project_in_the_red_up_to_zero_and_the_balance()
    {
        BoxRules.CheckTransfer(300, balance: 1000, cashProfit: -500).ShouldBeNull();
        BoxRules.CheckTransfer(500, balance: 500, cashProfit: -500).ShouldBeNull();
        BoxRules.CheckTransfer(100, balance: 1000, cashProfit: 0)!.Code.ShouldBe(ErrorCodes.BoxProjectNotInRed);
        BoxRules.CheckTransfer(600, balance: 1000, cashProfit: -500)!.Field.ShouldBe("amount");
        BoxRules.CheckTransfer(400, balance: 300, cashProfit: -500)!.Code.ShouldBe(ErrorCodes.BoxShort);
    }

    [Fact]
    public void Only_a_pending_project_is_decided_and_only_to_remove_or_keep()
    {
        BoxRules.CheckDecision(BoxState.Pending).ShouldBeNull();
        BoxRules.CheckDecision(BoxState.In)!.Code.ShouldBe(ErrorCodes.BoxNoDecision);
        BoxRules.CheckDecision(BoxState.Removed)!.Code.ShouldBe(ErrorCodes.BoxNoDecision);
        BoxRules.CheckDecisionChoice(BoxChoice.Remove).ShouldBeNull();
        BoxRules.CheckDecisionChoice(BoxChoice.Keep).ShouldBeNull();
        BoxRules.CheckDecisionChoice(BoxChoice.Fresh).ShouldNotBeNull();
    }
}
