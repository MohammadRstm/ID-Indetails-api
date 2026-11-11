using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Company;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class OneOffExpenseRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    [Fact]
    public void A_one_off_is_dated_from_the_first_of_this_month_up_to_today()
    {
        OneOffExpenseRules.CheckSpentOn(new(2026, 11, 1), Today).ShouldBeNull();
        OneOffExpenseRules.CheckSpentOn(Today, Today).ShouldBeNull();

        foreach (var day in new DateOnly[] { new(2026, 10, 31), new(2026, 11, 12), new(2027, 11, 5) })
        {
            var denial = OneOffExpenseRules.CheckSpentOn(day, Today)!;
            denial.Code.ShouldBe(ErrorCodes.OneOffOutsideThisMonth);
            denial.Kind.ShouldBe(DenialKind.Validation);
            denial.Field.ShouldBe("spentOn");
        }
    }

    [Fact]
    public void A_one_off_can_be_changed_only_while_its_month_is_the_current_one()
    {
        OneOffExpenseRules.IsOpen(new(2026, 11, 1), Today).ShouldBeTrue();
        OneOffExpenseRules.CheckOpen(new(2026, 11, 3), Today).ShouldBeNull();

        OneOffExpenseRules.IsOpen(new(2026, 10, 31), Today).ShouldBeFalse();
        var denial = OneOffExpenseRules.CheckOpen(new(2026, 10, 31), Today)!;
        denial.Code.ShouldBe(ErrorCodes.OneOffLocked);
        denial.Kind.ShouldBe(DenialKind.BusinessRule);
    }
}
