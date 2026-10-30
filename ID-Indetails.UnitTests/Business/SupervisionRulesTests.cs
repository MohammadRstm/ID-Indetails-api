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

    [Fact]
    public void No_more_than_is_left_can_be_taken_so_the_limit_itself_is_allowed()
    {
        SupervisionRules.CheckAmount(amount: 6_000m, left: 6_000m).ShouldBeNull();
        SupervisionRules.CheckAmount(amount: 0.01m, left: 6_000m).ShouldBeNull();

        var above = SupervisionRules.CheckAmount(amount: 6_000.01m, left: 6_000m)!;
        above.Code.ShouldBe(ErrorCodes.ValidationFailed);
        above.Kind.ShouldBe(DenialKind.Validation);
        above.Field.ShouldBe("amountUsd");
        above.Message.ShouldContain("6,000.00");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50.25)]
    public void Nothing_can_be_taken_when_nothing_is_left_or_it_was_over_taken(double left)
    {
        var denial = SupervisionRules.CheckAmount(amount: 1m, left: (decimal)left)!;

        denial.Field.ShouldBe("amountUsd");
        denial.Message.ShouldBe("Nothing of the supervision is left to take.");
    }
}
