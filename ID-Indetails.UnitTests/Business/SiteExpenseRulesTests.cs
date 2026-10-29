using FluentValidation.TestHelper;
using ID_Indetails.api.Business.SiteExpenses;
using ID_Indetails.api.DTO.Request.SiteExpenses;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.SiteExpenses;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class SiteExpenseRulesTests
{
    [Fact]
    public void Cost_is_quantity_times_unit_cost_when_both_are_known_otherwise_the_total()
    {
        SiteExpenseRules.Cost(12, 1500, null).ShouldBe(18_000m);
        SiteExpenseRules.Cost(3, 0.3333m, null).ShouldBe(1.00m);
        SiteExpenseRules.Cost(null, null, 3200).ShouldBe(3200m);
        SiteExpenseRules.Cost(5, null, 90).ShouldBe(90m);
    }

    [Fact]
    public void A_total_typed_over_quantity_and_unit_cost_sets_the_unit_cost()
    {
        SiteExpenseRules.Normalise(new(10, 2, 25)).ShouldBe(new SiteExpenseRules.Entry(10, 2.5m, null));
        SiteExpenseRules.Normalise(new(3, 33.3333m, 100)).ShouldBe(new SiteExpenseRules.Entry(3, 33.3333m, null));
        SiteExpenseRules.Normalise(new(10, 2, 20)).ShouldBe(new SiteExpenseRules.Entry(10, 2, null));
        SiteExpenseRules.Normalise(new(10, 2, null)).ShouldBe(new SiteExpenseRules.Entry(10, 2, null));
    }

    [Fact]
    public void A_lump_sum_is_kept_when_quantity_times_unit_cost_cannot_give_the_cost()
    {
        SiteExpenseRules.Normalise(new(null, null, 3200)).ShouldBe(new SiteExpenseRules.Entry(null, null, 3200));
        SiteExpenseRules.Normalise(new(6, null, 3200)).ShouldBe(new SiteExpenseRules.Entry(6, null, 3200));
    }

    [Fact]
    public void Expenses_are_grouped_by_their_text_in_any_case_or_spacing()
    {
        SiteExpenseRules.GroupKey("  Ready-mix   Concrete C30 ").ShouldBe("ready-mix concrete c30");
        SiteExpenseRules.GroupKey("ready-mix concrete c30").ShouldBe(SiteExpenseRules.GroupKey("READY-MIX CONCRETE C30"));
    }

    [Fact]
    public void An_expense_needs_an_item_and_a_cost()
    {
        var validator = new SiteExpenseEntryRequestValidator();

        validator.TestValidate(new SiteExpenseEntryRequest { Name = "Rebar", Quantity = 2, UnitCost = 10 }).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new SiteExpenseEntryRequest { Name = "Rebar", Amount = 50 }).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new SiteExpenseEntryRequest { Name = "Rebar", Quantity = 2 }).ShouldHaveValidationErrorFor(x => x.Amount);
        validator.TestValidate(new SiteExpenseEntryRequest { Name = " ", Amount = 50 }).ShouldHaveValidationErrorFor(x => x.Name);
        validator.TestValidate(new SiteExpenseEntryRequest { Name = "Rebar", Quantity = 1.2345m, UnitCost = 1 }).ShouldHaveValidationErrorFor(x => x.Quantity);
        validator.TestValidate(new SiteExpenseEntryRequest { Name = "Rebar", Quantity = 1, UnitCost = 1.23456m }).ShouldHaveValidationErrorFor(x => x.UnitCost);
    }

    [Fact]
    public void Currency_date_category_and_label_are_optional_but_checked_when_given()
    {
        var validator = new SiteExpenseEntryRequestValidator();
        var expense = new SiteExpenseEntryRequest { Name = "Crane hire", Amount = 900 };

        validator.TestValidate(expense).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(expense with
        {
            Currency = "lbp", SpentOn = new DateOnly(2026, 10, 1), Category = SiteExpenseCategory.Contractor, AccountLabelId = 2,
        }).ShouldNotHaveAnyValidationErrors();

        validator.TestValidate(expense with { Currency = "EUR" }).ShouldHaveValidationErrorFor(x => x.Currency);
        validator.TestValidate(expense with { SpentOn = new DateOnly(1999, 12, 31) }).ShouldHaveValidationErrorFor(x => x.SpentOn);
        validator.TestValidate(expense with { Category = (SiteExpenseCategory)99 }).ShouldHaveValidationErrorFor(x => x.Category);
        validator.TestValidate(expense with { AccountLabelId = 0 }).ShouldHaveValidationErrorFor(x => x.AccountLabelId);
    }
}
