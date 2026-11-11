using ID_Indetails.api.DTO.Request.Insights;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Insights;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class InsightValidatorTests
{
    private static IEnumerable<string> Errors(ProjectScenarioQuery query)
        => new ProjectScenarioQueryValidator().Validate(query).Errors.Select(e => e.PropertyName);

    private static bool Valid(ProjectScenarioQuery query) => new ProjectScenarioQueryValidator().Validate(query).IsValid;

    [Fact]
    public void A_scenario_needs_at_least_one_change()
    {
        var validator = new ProjectScenarioQueryValidator();

        validator.Validate(new ProjectScenarioQuery()).Errors.ShouldHaveSingleItem()
            .ErrorMessage.ShouldBe("Choose at least one change to simulate.");

        // a source alone is no change
        var sourceOnly = validator.Validate(new ProjectScenarioQuery { ExtraCostSource = CostSource.Labour }).Errors;
        sourceOnly.Select(e => e.ErrorMessage).ShouldContain("Choose at least one change to simulate.");
        sourceOnly.Select(e => e.PropertyName).ShouldContain("ExtraCost");
    }

    [Fact]
    public void An_extra_cost_needs_its_source_and_a_source_its_amount()
    {
        Errors(new ProjectScenarioQuery { ExtraCost = 500m }).ShouldBe(["ExtraCostSource"]);
        Errors(new ProjectScenarioQuery { VoAmount = 100m, ExtraCostSource = CostSource.Labour }).ShouldBe(["ExtraCost"]);
        Errors(new ProjectScenarioQuery { ExtraCost = 0m, ExtraCostSource = CostSource.Expenses }).ShouldBe(["ExtraCost"]);
        Errors(new ProjectScenarioQuery { ExtraCost = -10m, ExtraCostSource = CostSource.Expenses }).ShouldBe(["ExtraCost"]);
        Errors(new ProjectScenarioQuery { ExtraCost = ProjectScenarioQueryValidator.MaxAmount + 1, ExtraCostSource = CostSource.Expenses })
            .ShouldBe(["ExtraCost"]);
        Valid(new ProjectScenarioQuery { ExtraCost = 500m, ExtraCostSource = CostSource.SiteExpense }).ShouldBeTrue();
    }

    [Fact]
    public void Rates_are_fractions_between_0_and_1()
    {
        Errors(new ProjectScenarioQuery { SupervisionRate = 1.5m }).ShouldBe(["SupervisionRate"]);
        Errors(new ProjectScenarioQuery { SupervisionRate = -0.01m }).ShouldBe(["SupervisionRate"]);
        Errors(new ProjectScenarioQuery { SupervisionRate = 0.12345m }).ShouldBe(["SupervisionRate"]);
        Valid(new ProjectScenarioQuery { SupervisionRate = 0m }).ShouldBeTrue();
        Valid(new ProjectScenarioQuery { SupervisionRate = 1m }).ShouldBeTrue();

        Errors(new ProjectScenarioQuery { TvaRate = 1.01m }).ShouldBe(["TvaRate"]);
        Errors(new ProjectScenarioQuery { TvaRate = -0.01m }).ShouldBe(["TvaRate"]);
        Valid(new ProjectScenarioQuery { TvaRate = 0m }).ShouldBeTrue();
    }

    [Fact]
    public void A_delay_is_1_to_104_weeks()
    {
        Errors(new ProjectScenarioQuery { DelayWeeks = 0 }).ShouldBe(["DelayWeeks"]);
        Errors(new ProjectScenarioQuery { DelayWeeks = 105 }).ShouldBe(["DelayWeeks"]);
        Valid(new ProjectScenarioQuery { DelayWeeks = 1 }).ShouldBeTrue();
        Valid(new ProjectScenarioQuery { DelayWeeks = ProjectScenarioQueryValidator.MaxDelayWeeks }).ShouldBeTrue();
    }

    [Fact]
    public void A_variation_order_is_never_zero()
    {
        Errors(new ProjectScenarioQuery { VoAmount = 0m }).ShouldBe(["VoAmount"]);
        Errors(new ProjectScenarioQuery { VoAmount = ProjectScenarioQueryValidator.MaxAmount + 1 }).ShouldBe(["VoAmount"]);
        Valid(new ProjectScenarioQuery { VoAmount = -5_000m }).ShouldBeTrue();
    }

    [Fact]
    public void A_variation_order_and_a_settlement_are_simulated_apart()
    {
        var error = new ProjectScenarioQueryValidator()
            .Validate(new ProjectScenarioQuery { VoAmount = 1_000m, SettleOwed = OwedSettlement.Excused })
            .Errors.ShouldHaveSingleItem();
        error.PropertyName.ShouldBe("VoAmount");
        error.ErrorMessage.ShouldBe("A settlement sets the price to what the client paid, so a variation order would change nothing: simulate them apart.");

        Valid(new ProjectScenarioQuery { SettleOwed = OwedSettlement.WrittenOff }).ShouldBeTrue();
        Valid(new ProjectScenarioQuery { SettleOwed = OwedSettlement.Excused, ExtraCost = 100m, ExtraCostSource = CostSource.Expenses }).ShouldBeTrue();
    }

    [Fact]
    public void Changes_combine()
    {
        Valid(new ProjectScenarioQuery
        {
            VoAmount = 10_000m,
            ExtraCost = 500m,
            ExtraCostSource = CostSource.SiteExpense,
            SupervisionRate = 0.12m,
            TvaRate = 0.11m,
            DelayWeeks = 4,
        }).ShouldBeTrue();
        Valid(new ProjectScenarioQuery { SettleOwed = OwedSettlement.Excused, TvaRate = 0.05m, DelayWeeks = 2 }).ShouldBeTrue();
    }

    [Fact]
    public void Expense_trends_cover_1_to_24_months_and_12_when_left_out()
    {
        var validator = new ExpenseTrendsQueryValidator();

        validator.Validate(new ExpenseTrendsQuery()).IsValid.ShouldBeTrue();
        ExpenseTrendsQueryValidator.DefaultMonths.ShouldBe(12);
        validator.Validate(new ExpenseTrendsQuery { Months = 1 }).IsValid.ShouldBeTrue();
        validator.Validate(new ExpenseTrendsQuery { Months = 24 }).IsValid.ShouldBeTrue();

        validator.Validate(new ExpenseTrendsQuery { Months = 0 }).Errors.Select(e => e.PropertyName).ShouldBe(["Months"]);
        validator.Validate(new ExpenseTrendsQuery { Months = 25 }).Errors.Select(e => e.PropertyName).ShouldBe(["Months"]);
    }
}
