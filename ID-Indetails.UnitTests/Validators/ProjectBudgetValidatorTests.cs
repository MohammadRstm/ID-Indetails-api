using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.Validators.Projects;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class ProjectBudgetValidatorTests
{
    private static readonly SetProjectBudgetRequestValidator Validator = new();

    private static SetProjectBudgetRequest Request(params ProjectBudgetLineRequest[] lines)
        => new() { Reason = "Agreed with the client", Lines = lines };

    private static IReadOnlyList<string> Errors(SetProjectBudgetRequest request)
        => Validator.Validate(request).Errors.Select(e => e.PropertyName).Distinct().ToList();

    [Fact]
    public void A_budget_has_a_reason_and_one_to_fifty_lines()
    {
        Validator.Validate(Request(new ProjectBudgetLineRequest(4, null, 5000m), new ProjectBudgetLineRequest(null, "expenses", 0m)))
            .IsValid.ShouldBeTrue();

        Errors(Request(new ProjectBudgetLineRequest(4, null, 5000m)) with { Reason = "  " }).ShouldBe(["Reason"]);
        Errors(Request(new ProjectBudgetLineRequest(4, null, 5000m)) with { Reason = new string('x', BudgetRules.MaxReasonLength + 1) })
            .ShouldBe(["Reason"]);
        Errors(Request()).ShouldBe(["Lines"]);
        Errors(Request(Enumerable.Range(1, BudgetRules.MaxLines + 1).Select(i => new ProjectBudgetLineRequest(i, null, 1m)).ToArray()))
            .ShouldBe(["Lines"]);
        Validator.Validate(Request(Enumerable.Range(1, BudgetRules.MaxLines).Select(i => new ProjectBudgetLineRequest(i, null, 1m)).ToArray()))
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Each_line_is_a_department_or_a_known_category_budgeted_once()
    {
        Errors(Request(new ProjectBudgetLineRequest(null, null, 10m))).ShouldBe(["Lines[0].TeamId"]);
        Errors(Request(new ProjectBudgetLineRequest(4, "expenses", 10m))).ShouldBe(["Lines[0].TeamId"]);
        Errors(Request(new ProjectBudgetLineRequest(0, null, 10m))).ShouldBe(["Lines[0].TeamId"]);
        Errors(Request(new ProjectBudgetLineRequest(null, "plumbing", 10m))).ShouldBe(["Lines[0].Category"]);
        Errors(Request(new ProjectBudgetLineRequest(null, "Materials", 10m))).ShouldBe(["Lines[0].Category"]);
        Errors(Request(new ProjectBudgetLineRequest(4, null, 10m), new ProjectBudgetLineRequest(4, null, 20m))).ShouldBe(["Lines"]);
        Errors(Request(new ProjectBudgetLineRequest(null, "fees", 10m), new ProjectBudgetLineRequest(null, "fees", 20m))).ShouldBe(["Lines"]);
    }

    [Fact]
    public void An_amount_is_zero_to_a_hundred_million_in_cents()
    {
        Validator.Validate(Request(new ProjectBudgetLineRequest(null, "materials", 0m))).IsValid.ShouldBeTrue();
        Validator.Validate(Request(new ProjectBudgetLineRequest(null, "materials", BudgetRules.MaxAmount))).IsValid.ShouldBeTrue();
        Errors(Request(new ProjectBudgetLineRequest(null, "materials", BudgetRules.MaxAmount + 0.01m))).ShouldBe(["Lines[0].Amount"]);
        Errors(Request(new ProjectBudgetLineRequest(null, "materials", -1m))).ShouldBe(["Lines[0].Amount"]);
        Errors(Request(new ProjectBudgetLineRequest(null, "materials", 10.555m))).ShouldBe(["Lines[0].Amount"]);
    }

    [Fact]
    public void The_start_is_optional_and_in_this_century()
    {
        Validator.Validate(Request(new ProjectBudgetLineRequest(4, null, 1m)) with { EffectiveFrom = new DateOnly(2026, 11, 1) }).IsValid.ShouldBeTrue();
        Errors(Request(new ProjectBudgetLineRequest(4, null, 1m)) with { EffectiveFrom = new DateOnly(1999, 12, 31) }).ShouldBe(["EffectiveFrom"]);
    }
}
