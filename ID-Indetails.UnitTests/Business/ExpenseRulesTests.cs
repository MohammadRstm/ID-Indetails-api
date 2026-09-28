using ID_Indetails.api.Business.Expenses;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Expenses;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ExpenseRulesTests
{
    [Fact]
    public void Expenses_change_only_while_the_project_is_open_and_unlocked()
    {
        ExpenseRules.CheckEditable(ProjectStatus.Active, revenueLocked: false).ShouldBeNull();
        ExpenseRules.CheckEditable(ProjectStatus.Hold, revenueLocked: false).ShouldBeNull();
        ExpenseRules.CheckEditable(ProjectStatus.Completed, revenueLocked: true)!.Code.ShouldBe(ErrorCodes.ProjectClosed);
        ExpenseRules.CheckEditable(ProjectStatus.Archived, revenueLocked: true)!.Code.ShouldBe(ErrorCodes.ProjectClosed);
        ExpenseRules.CheckEditable(ProjectStatus.Active, revenueLocked: true)!.Code.ShouldBe(ErrorCodes.ProjectClosed);
    }

    [Fact]
    public void Totals_are_kept_per_currency_and_never_mixed()
    {
        var totals = ExpenseRules.Totals([("USD", 100.50m), ("LBP", 900_000m), ("USD", 49.50m)]);

        totals.ShouldBe([new("LBP", 900_000m), new("USD", 150m)]);
        ExpenseRules.Totals([]).ShouldBeEmpty();
    }

    [Fact]
    public void An_expense_needs_a_label_a_positive_amount_a_known_currency_and_category()
    {
        var validator = new CreateExpenseRequestValidator();

        validator.Validate(new CreateExpenseRequest { Label = "Licence", Amount = 120.25m, Currency = "usd", Category = "software" })
            .IsValid.ShouldBeTrue();
        validator.Validate(new CreateExpenseRequest { Label = " ", Amount = 0, Currency = "EUR", Category = "food" })
            .Errors.Select(e => e.PropertyName).Distinct().ShouldBe(["Label", "Amount", "Currency", "Category"], ignoreOrder: true);
        validator.Validate(new CreateExpenseRequest { Label = "Taxi", Amount = 10.125m }).IsValid.ShouldBeFalse();
    }
}
