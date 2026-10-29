using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class SupervisionRulesTests
{
    [Fact]
    public void Only_execution_client_sites_have_supervision_taken_and_account_figures()
    {
        SupervisionRules.CheckSite(DomainCodes.Execution, BillingType.Client).ShouldBeNull();
        SupervisionRules.CheckSite(DomainCodes.Design, BillingType.Client)!.Code.ShouldBe(ErrorCodes.NotForThisDomain);
        SupervisionRules.CheckSite(DomainCodes.Execution, BillingType.Internal)!.Kind.ShouldBe(DenialKind.BusinessRule);
    }

    [Fact]
    public void Supervision_can_be_taken_today_or_on_an_earlier_day_but_not_a_future_one()
    {
        var today = new DateOnly(2026, 10, 29);

        SupervisionRules.CheckTakenOn(today, today).ShouldBeNull();
        SupervisionRules.CheckTakenOn(today.AddDays(-400), today).ShouldBeNull();

        var tomorrow = SupervisionRules.CheckTakenOn(today.AddDays(1), today)!;
        tomorrow.Code.ShouldBe(ErrorCodes.TakenDateInFuture);
        tomorrow.Kind.ShouldBe(DenialKind.Validation);
        tomorrow.Field.ShouldBe("takenOn");
    }

    [Fact]
    public void What_is_left_is_the_supervision_due_less_what_was_taken_and_negative_once_over()
    {
        SupervisionRules.Left(due: 63_011.70m, taken: 0).ShouldBe(63_011.70m);
        SupervisionRules.Left(due: 63_011.70m, taken: 63_011.70m).ShouldBe(0);
        SupervisionRules.Left(due: 100, taken: 130.50m).ShouldBe(-30.50m);
    }
}
