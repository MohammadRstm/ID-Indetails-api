using ID_Indetails.api.Business.Company;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class AccountLabelRulesTests
{
    [Fact]
    public void A_new_label_goes_last()
    {
        AccountLabelRules.NextPosition([]).ShouldBe(1);
        AccountLabelRules.NextPosition([1, 2, 3]).ShouldBe(4);
        AccountLabelRules.NextPosition([3, 1]).ShouldBe(4);
    }

    [Fact]
    public void A_new_order_lists_every_label_that_can_be_picked_once()
    {
        AccountLabelRules.CheckOrder([1, 2, 3], [3, 1, 2]).ShouldBeNull();
        AccountLabelRules.CheckOrder([], []).ShouldBeNull();

        foreach (var order in new long[][] { [1, 2], [1, 2, 3, 4], [1, 1, 2], [1, 2, 9] })
        {
            AccountLabelRules.CheckOrder([1, 2, 3], order)!.Code.ShouldBe(ErrorCodes.AccountLabelOrderStale);
        }
    }
}
