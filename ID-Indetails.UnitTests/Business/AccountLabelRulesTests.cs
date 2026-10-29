using ID_Indetails.api.Business.Common;
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
    public void A_label_can_be_picked_when_it_exists_and_is_not_archived_unless_the_record_already_has_it()
    {
        AccountLabelRules.CheckPickable(exists: true, archived: false, alreadyHasIt: false, "accountLabelId").ShouldBeNull();
        AccountLabelRules.CheckPickable(exists: true, archived: true, alreadyHasIt: true, "accountLabelId").ShouldBeNull();

        var archived = AccountLabelRules.CheckPickable(exists: true, archived: true, alreadyHasIt: false, "accountLabelId")!;
        archived.Code.ShouldBe(ErrorCodes.AccountLabelArchived);
        archived.Kind.ShouldBe(DenialKind.BusinessRule);

        var missing = AccountLabelRules.CheckPickable(exists: false, archived: false, alreadyHasIt: false, "items[2].accountLabelId")!;
        missing.Kind.ShouldBe(DenialKind.Validation);
        missing.Field.ShouldBe("items[2].accountLabelId");
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
