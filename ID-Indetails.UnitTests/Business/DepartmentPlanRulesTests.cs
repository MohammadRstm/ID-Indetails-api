using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class DepartmentPlanRulesTests
{
    private static readonly long[] OnProject = [1, 2];

    [Theory]
    [InlineData(120, 7_200)]
    [InlineData(40.5, 2_430)]
    [InlineData(0.01, 1)]
    [InlineData(1.99, 119)]
    [InlineData(100_000, 6_000_000)]
    public void Planned_hours_are_kept_in_whole_minutes(double hours, int minutes)
        => DepartmentPlanRules.MinutesOf((decimal)hours).ShouldBe(minutes);

    [Fact]
    public void Only_the_project_s_own_departments_are_planned_and_each_once()
    {
        DepartmentPlanRules.Check([1], OnProject).ShouldBeNull();
        DepartmentPlanRules.Check([2, 1], OnProject).ShouldBeNull();

        var notOn = DepartmentPlanRules.Check([1, 3], OnProject).ShouldNotBeNull();
        (notOn.Kind, notOn.Code, notOn.Field).ShouldBe((DenialKind.Validation, ErrorCodes.ValidationFailed, "departments[1].teamId"));

        var twice = DepartmentPlanRules.Check([1, 1], OnProject).ShouldNotBeNull();
        (twice.Kind, twice.Field).ShouldBe((DenialKind.Validation, "departments"));

        // a project without departments has none to plan
        DepartmentPlanRules.Check([1], []).ShouldNotBeNull();
    }

    [Fact]
    public void Progress_is_logged_over_planned_minutes_to_four_places_and_may_pass_one()
    {
        DepartmentPlanRules.Progress(180, 240).ShouldBe(0.75m);
        DepartmentPlanRules.Progress(0, 240).ShouldBe(0m);
        DepartmentPlanRules.Progress(240, 240).ShouldBe(1m);
        DepartmentPlanRules.Progress(300, 240).ShouldBe(1.25m);
        // 100 of 300 minutes is a third, rounded to four places; 3 of 60,000 is 0.00005, away from zero to 0.0001
        DepartmentPlanRules.Progress(100, 300).ShouldBe(0.3333m);
        DepartmentPlanRules.Progress(3, 60_000).ShouldBe(0.0001m);
        DepartmentPlanRules.Progress(200, 300).ShouldBe(0.6667m);
    }

    [Fact]
    public void A_department_without_a_plan_has_no_progress()
    {
        DepartmentPlanRules.Progress(180, null).ShouldBeNull();
        DepartmentPlanRules.Progress(0, null).ShouldBeNull();
        DepartmentPlanRules.Progress(180, 0).ShouldBeNull();
    }
}
