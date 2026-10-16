using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Proton.Usage;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonBudgetHoldTests
{
    [Theory]
    [InlineData(1.00, 0.00, 0.25, true)]
    [InlineData(1.00, 0.75, 0.25, true)]
    [InlineData(1.00, 0.80, 0.25, false)]
    [InlineData(0.10, 0.00, 0.25, false)]
    public void A_question_may_hold_only_what_the_budget_still_covers(decimal remaining, decimal held, decimal amount, bool allowed)
        => ProtonUsageRules.CanHold(remaining, held, amount).ShouldBe(allowed);

    [Fact]
    public void Holds_add_up_while_questions_are_answered_and_go_once_each_is_done()
    {
        var holds = new ProtonBudgetHolds();
        var first = holds.Add(0.25m);
        var second = holds.Add(0.25m);
        holds.Held.ShouldBe(0.50m);

        first.Dispose();
        first.Dispose();
        holds.Held.ShouldBe(0.25m);

        second.Dispose();
        holds.Held.ShouldBe(0m);
    }
}
