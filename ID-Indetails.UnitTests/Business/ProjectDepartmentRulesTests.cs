using System.Globalization;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using Shouldly;
using Candidate = ID_Indetails.api.Business.Projects.ProjectDepartmentRules.Candidate;

namespace ID_Indetails.UnitTests.Business;

public class ProjectDepartmentRulesTests
{
    private static readonly Candidate Architecture = new(1, "Architecture", DomainCodes.Design);
    private static readonly Candidate Structure = new(2, "Structure", DomainCodes.Design);
    private static readonly Candidate SiteOperations = new(9, "Site Operations", DomainCodes.Execution);

    [Theory]
    [InlineData(1, new[] { "1" })]
    [InlineData(2, new[] { "0.5", "0.5" })]
    [InlineData(3, new[] { "0.3334", "0.3333", "0.3333" })]
    [InlineData(4, new[] { "0.25", "0.25", "0.25", "0.25" })]
    [InlineData(6, new[] { "0.1667", "0.1667", "0.1667", "0.1667", "0.1666", "0.1666" })]
    public void Equal_shares_add_up_to_exactly_one(int count, string[] expected)
    {
        var shares = ProjectDepartmentRules.EqualShares(count);

        shares.ShouldBe(expected.Select(e => decimal.Parse(e, CultureInfo.InvariantCulture)).ToList());
        shares.Sum().ShouldBe(1m);
    }

    [Fact]
    public void Departments_are_Design_ones_chosen_once_and_may_be_left_out()
    {
        ProjectDepartmentRules.CheckChosen(DomainCodes.Design, [1, 2], [Architecture, Structure]).ShouldBeNull();
        // none: the project works as before departments
        ProjectDepartmentRules.CheckChosen(DomainCodes.Design, [], []).ShouldBeNull();
        ProjectDepartmentRules.CheckChosen(DomainCodes.Execution, [], []).ShouldBeNull();

        ProjectDepartmentRules.CheckChosen(DomainCodes.Design, [1, 1], [Architecture])!.Message.ShouldBe("Choose each department once.");
        ProjectDepartmentRules.CheckChosen(DomainCodes.Design, [1, 9], [Architecture, SiteOperations])!.Field.ShouldBe("departmentIds");
        ProjectDepartmentRules.CheckChosen(DomainCodes.Design, [1, 5], [Architecture])!.Message.ShouldBe("Choose departments of Design.");
        ProjectDepartmentRules.CheckChosen(DomainCodes.Execution, [9], [SiteOperations])!.Message.ShouldBe("Only Design projects have departments.");
    }

    [Fact]
    public void Shares_go_to_each_department_once_and_add_up_to_100_percent()
    {
        ProjectDepartmentRules.CheckShares([1, 2], [(1, 0.6m), (2, 0.4m)]).ShouldBeNull();
        ProjectDepartmentRules.CheckShares([1, 2], [(1, 1m), (2, 0m)]).ShouldBeNull();

        ProjectDepartmentRules.CheckShares([1, 2], [(1, 0.6m), (2, 0.3m)])!.Message.ShouldBe("The shares must add up to 100%.");
        ProjectDepartmentRules.CheckShares([1, 2], [(1, 1m)])!.Field.ShouldBe("shares");
        ProjectDepartmentRules.CheckShares([1, 2], [(1, 0.5m), (3, 0.5m)])!.Field.ShouldBe("shares");
        ProjectDepartmentRules.CheckShares([1, 2], [(1, 0.5m), (1, 0.5m)])!.Field.ShouldBe("shares");
        ProjectDepartmentRules.CheckShares([1, 2], [(1, 1.2m), (2, -0.2m)])!.Message.ShouldContain("between 0% and 100%");
        ProjectDepartmentRules.CheckShares([1, 2], [(1, 0.33335m), (2, 0.66665m)])!.Message.ShouldContain("two decimal places");
    }

    [Fact]
    public void A_department_with_hours_stays_on_the_project()
    {
        ProjectDepartmentRules.CheckRemoval([]).ShouldBeNull();
        ProjectDepartmentRules.CheckRemoval(["Structure"])!.Code.ShouldBe(ErrorCodes.DepartmentHasHours);
        ProjectDepartmentRules.CheckRemoval(["Interior", "Structure"])!.Message
            .ShouldBe("Interior and Structure have hours or variation orders on this project, so they can't be taken off it.");
    }

    [Fact]
    public void Every_department_on_a_project_makes_it_a_full_file()
    {
        ProjectDepartmentRules.IsFullFile([1, 2, 3], [1, 2, 3]).ShouldBeTrue();
        ProjectDepartmentRules.IsFullFile([1, 2], [1, 2, 3]).ShouldBeFalse();
        // one department is just that department
        ProjectDepartmentRules.IsFullFile([1], [1]).ShouldBeFalse();
    }

    [Fact]
    public void Only_the_projects_departments_log_on_it_and_a_project_with_none_takes_anyone()
    {
        ProjectDepartmentRules.MayLog([1, 2], 1).ShouldBeTrue();
        ProjectDepartmentRules.MayLog([1, 2], 3).ShouldBeFalse();
        ProjectDepartmentRules.MayLog([1, 2], null).ShouldBeFalse();
        ProjectDepartmentRules.MayLog([], null).ShouldBeTrue();
        ProjectDepartmentRules.MayLog([], 3).ShouldBeTrue();
    }
}
