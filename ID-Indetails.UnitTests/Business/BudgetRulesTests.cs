using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using Cost = ID_Indetails.api.Business.Projects.BudgetRules.Cost;
using Line = ID_Indetails.api.Business.Projects.BudgetRules.Line;

namespace ID_Indetails.UnitTests.Business;

public class BudgetRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly long[] Departments = [1, 2];

    private static RuleDenial? Check(string domain, params Line[] lines) => BudgetRules.Check(domain, lines, Departments, Today, Today);

    [Fact]
    public void Each_business_has_its_own_categories()
    {
        BudgetRules.CategoriesOf(DomainCodes.Design).ShouldBe(["expenses"]);
        BudgetRules.CategoriesOf(DomainCodes.Execution).ShouldBe(["materials", "contractor", "equipment", "fees", "other", "ground_labour"]);
        BudgetRules.AllCategories.Count.ShouldBe(7);
        // every site expense category has a line of its own
        Enum.GetValues<SiteExpenseCategory>().Select(BudgetRules.CategoryOf)
            .ShouldBe(BudgetRules.CategoriesOf(DomainCodes.Execution).Except([BudgetRules.GroundLabour]));
        BudgetRules.NameOf("ground_labour").ShouldBe("Ground labour");
        BudgetRules.NameOf("expenses").ShouldBe("Expenses");
        BudgetRules.NameOf("materials").ShouldBe("Materials");
    }

    [Fact]
    public void A_design_budget_has_the_projects_departments_and_its_expenses()
    {
        Check(DomainCodes.Design, new Line(1, null, 5000), new Line(2, null, 0), new Line(null, "expenses", 800)).ShouldBeNull();

        var stranger = Check(DomainCodes.Design, new Line(1, null, 5000), new Line(7, null, 100))!;
        stranger.Kind.ShouldBe(DenialKind.Validation);
        stranger.Code.ShouldBe(ErrorCodes.BudgetLineInvalid);
        stranger.Field.ShouldBe("lines[1].teamId");

        var siteCategory = Check(DomainCodes.Design, new Line(null, "materials", 100))!;
        siteCategory.Code.ShouldBe(ErrorCodes.BudgetLineInvalid);
        siteCategory.Field.ShouldBe("lines[0].category");
        Check(DomainCodes.Design, new Line(null, "ground_labour", 100))!.Field.ShouldBe("lines[0].category");
    }

    [Fact]
    public void A_site_budget_has_its_categories_and_ground_labour_and_no_departments()
    {
        Check(DomainCodes.Execution,
            new Line(null, "materials", 20000), new Line(null, "contractor", 1), new Line(null, "equipment", 0),
            new Line(null, "fees", 5), new Line(null, "other", 5), new Line(null, "ground_labour", 9000)).ShouldBeNull();

        Check(DomainCodes.Execution, new Line(1, null, 100))!.Field.ShouldBe("lines[0].teamId");
        Check(DomainCodes.Execution, new Line(null, "expenses", 100))!.Field.ShouldBe("lines[0].category");
        Check(DomainCodes.Execution, new Line(null, null, 100))!.Field.ShouldBe("lines[0].category");
    }

    [Fact]
    public void A_line_is_budgeted_once()
    {
        Check(DomainCodes.Design, new Line(1, null, 100), new Line(1, null, 200))!.Field.ShouldBe("lines");
        Check(DomainCodes.Execution, new Line(null, "fees", 100), new Line(null, "fees", 100))!.Code.ShouldBe(ErrorCodes.BudgetLineInvalid);
    }

    [Fact]
    public void A_budget_starts_today_or_earlier_never_later()
    {
        BudgetRules.Check(DomainCodes.Design, [new Line(1, null, 1)], Departments, Today, Today).ShouldBeNull();
        BudgetRules.Check(DomainCodes.Design, [new Line(1, null, 1)], Departments, Today.AddYears(-1), Today).ShouldBeNull();

        var tomorrow = BudgetRules.Check(DomainCodes.Design, [new Line(1, null, 1)], Departments, Today.AddDays(1), Today)!;
        tomorrow.Field.ShouldBe("effectiveFrom");
        tomorrow.Code.ShouldBe(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public void Each_line_holds_its_own_cost_and_the_rest_is_uncovered()
    {
        Line[] lines = [new(1, null, 5000), new(2, null, 1000), new(null, "expenses", 500)];
        Cost[] costs =
        [
            new(1, null, 1200.004m), new(1, null, 799.996m),
            new(2, null, 1500m),
            new(3, null, 300m), // a department no longer in the budget
            new(null, null, 40m), // hours in no department
            new(null, "expenses", 500m),
            new(null, "ground_labour", 0m),
        ];

        var figures = BudgetRules.Compare(lines, costs);

        figures.Lines.Select(l => (l.TeamId, l.Category, l.Amount, l.Cost, l.Left)).ShouldBe(
        [
            (1L, (string?)null, 5000m, 2000m, 3000m),
            (2L, null, 1000m, 1500m, -500m),
            (null, "expenses", 500m, 500m, 0m),
        ]);
        figures.Uncovered.ShouldBe(340m);
        BudgetRules.TotalOf(lines).ShouldBe(6500m);
    }

    [Fact]
    public void Without_a_budget_every_cost_is_uncovered()
    {
        var figures = BudgetRules.Compare([], [new Cost(null, "materials", 1000.005m), new Cost(null, BudgetRules.GroundLabour, 250m)]);

        figures.Lines.ShouldBeEmpty();
        figures.Uncovered.ShouldBe(1250.01m);
    }

    [Fact]
    public void A_site_category_line_holds_only_that_category()
    {
        var figures = BudgetRules.Compare(
            [new Line(null, "materials", 10000), new Line(null, BudgetRules.GroundLabour, 3000)],
            [new Cost(null, "materials", 4000), new Cost(null, "contractor", 2500), new Cost(null, BudgetRules.GroundLabour, 3200), new Cost(null, null, 75)]);

        figures.Lines.Select(l => l.Left).ShouldBe([6000m, -200m]);
        figures.Uncovered.ShouldBe(2575m);
    }
}
