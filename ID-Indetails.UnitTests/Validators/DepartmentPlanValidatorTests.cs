using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.Validators.Projects;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class DepartmentPlanValidatorTests
{
    private static readonly SetDepartmentPlanRequestValidator Validator = new();

    private static SetDepartmentPlanRequest Request(params DepartmentPlanRequest[] departments) => new() { Departments = departments };

    private static IReadOnlyList<string> Errors(SetDepartmentPlanRequest request)
        => Validator.Validate(request).Errors.Select(e => e.PropertyName).Distinct().ToList();

    [Fact]
    public void Planned_hours_are_above_zero_up_to_100000_to_two_places_or_null_to_clear()
    {
        Validator.Validate(Request(new DepartmentPlanRequest(4, 120m), new DepartmentPlanRequest(5, null))).IsValid.ShouldBeTrue();
        Validator.Validate(Request(new DepartmentPlanRequest(4, 0.01m))).IsValid.ShouldBeTrue();
        Validator.Validate(Request(new DepartmentPlanRequest(4, DepartmentPlanRules.MaxHours))).IsValid.ShouldBeTrue();

        Errors(Request(new DepartmentPlanRequest(4, 0m))).ShouldBe(["Departments[0].PlannedHours"]);
        Errors(Request(new DepartmentPlanRequest(4, -5m))).ShouldBe(["Departments[0].PlannedHours"]);
        Errors(Request(new DepartmentPlanRequest(4, 100_000.01m))).ShouldBe(["Departments[0].PlannedHours"]);
        Errors(Request(new DepartmentPlanRequest(4, 12.345m))).ShouldBe(["Departments[0].PlannedHours"]);
    }

    [Fact]
    public void One_to_fifty_departments_each_valid_and_planned_once()
    {
        Errors(Request()).ShouldBe(["Departments"]);
        Errors(Request(Enumerable.Range(1, DepartmentPlanRules.MaxDepartments + 1).Select(i => new DepartmentPlanRequest(i, 1m)).ToArray()))
            .ShouldBe(["Departments"]);
        Validator.Validate(Request(Enumerable.Range(1, DepartmentPlanRules.MaxDepartments).Select(i => new DepartmentPlanRequest(i, 1m)).ToArray()))
            .IsValid.ShouldBeTrue();

        Errors(Request(new DepartmentPlanRequest(4, 10m), new DepartmentPlanRequest(4, null))).ShouldBe(["Departments"]);
        Errors(Request(new DepartmentPlanRequest(0, 10m))).ShouldBe(["Departments[0].TeamId"]);
    }
}
