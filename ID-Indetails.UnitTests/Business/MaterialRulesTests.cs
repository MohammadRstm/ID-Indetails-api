using FluentValidation.TestHelper;
using ID_Indetails.api.Business.Materials;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Materials;
using ID_Indetails.api.Validators.Materials;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class MaterialRulesTests
{
    [Fact]
    public void Cost_is_quantity_times_unit_cost_when_both_are_known_otherwise_the_total()
    {
        MaterialRules.Cost(12, 1500, null).ShouldBe(18_000m);
        MaterialRules.Cost(3, 0.3333m, null).ShouldBe(1.00m);
        MaterialRules.Cost(null, null, 3200).ShouldBe(3200m);
        MaterialRules.Cost(5, null, 90).ShouldBe(90m);
    }

    [Fact]
    public void A_total_typed_over_quantity_and_unit_cost_sets_the_unit_cost()
    {
        MaterialRules.Normalise(new(10, 2, 25)).ShouldBe(new MaterialRules.Entry(10, 2.5m, null));
        MaterialRules.Normalise(new(3, 33.3333m, 100)).ShouldBe(new MaterialRules.Entry(3, 33.3333m, null));
        MaterialRules.Normalise(new(10, 2, 20)).ShouldBe(new MaterialRules.Entry(10, 2, null));
        MaterialRules.Normalise(new(10, 2, null)).ShouldBe(new MaterialRules.Entry(10, 2, null));
    }

    [Fact]
    public void A_lump_sum_is_kept_when_quantity_times_unit_cost_cannot_give_the_cost()
    {
        MaterialRules.Normalise(new(null, null, 3200)).ShouldBe(new MaterialRules.Entry(null, null, 3200));
        MaterialRules.Normalise(new(6, null, 3200)).ShouldBe(new MaterialRules.Entry(6, null, 3200));
    }

    [Fact]
    public void A_manual_price_equal_to_the_automatic_one_is_stored_as_automatic()
    {
        MaterialRules.ManualPrice(110, cost: 100, markup: 0.10m).ShouldBeNull();
        MaterialRules.ManualPrice(120, cost: 100, markup: 0.10m).ShouldBe(120m);
        MaterialRules.ManualPrice(null, cost: 100, markup: 0.10m).ShouldBeNull();
        MaterialRules.ManualPrice(0, cost: 100, markup: 0.10m).ShouldBe(0m);
    }

    [Fact]
    public void The_price_source_is_manual_when_a_price_was_set_and_automatic_otherwise()
    {
        MaterialRules.SourceOf(null).ShouldBe(MaterialRules.PriceSource.Auto);
        MaterialRules.SourceOf(99).ShouldBe(MaterialRules.PriceSource.Manual);
    }

    [Fact]
    public void Materials_are_grouped_by_their_text_in_any_case_or_spacing()
    {
        MaterialRules.GroupKey("  Ready-mix   Concrete C30 ").ShouldBe("ready-mix concrete c30");
        MaterialRules.GroupKey("ready-mix concrete c30").ShouldBe(MaterialRules.GroupKey("READY-MIX CONCRETE C30"));
    }

    [Fact]
    public void A_material_needs_a_type_and_a_cost()
    {
        var validator = new MaterialEntryRequestValidator();

        validator.TestValidate(new MaterialEntryRequest { Name = "Rebar", Quantity = 2, UnitCost = 10 }).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new MaterialEntryRequest { Name = "Rebar", Amount = 50 }).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new MaterialEntryRequest { Name = "Rebar", Quantity = 2 }).ShouldHaveValidationErrorFor(x => x.Amount);
        validator.TestValidate(new MaterialEntryRequest { Name = " ", Amount = 50 }).ShouldHaveValidationErrorFor(x => x.Name);
        validator.TestValidate(new MaterialEntryRequest { Name = "Rebar", Quantity = 1.2345m, UnitCost = 1 }).ShouldHaveValidationErrorFor(x => x.Quantity);
        validator.TestValidate(new MaterialEntryRequest { Name = "Rebar", Quantity = 1, UnitCost = 1.23456m }).ShouldHaveValidationErrorFor(x => x.UnitCost);
    }
}
