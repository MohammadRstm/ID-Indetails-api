using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Finance.Intelligence;
using ID_Indetails.api.Business.Labour;
using ID_Indetails.api.Enums;
using Shouldly;
using Risk = ID_Indetails.api.Business.Finance.Intelligence.ProjectRiskRules.Risk;

namespace ID_Indetails.UnitTests.Business;

public class ProjectRiskRulesTests
{
    /// <summary>The owner's thresholds: a site runs short within 24 working days; settlements are listed for 12 months.</summary>
    private static readonly ProjectRiskRules.Thresholds Owner = new(RunwayWorkingDays: 24, SettlementMonths: 12);

    private static readonly DateOnly Today = new(2026, 11, 10);

    /// <summary>A running client project with 10,000 agreed and 5,000 of profit, 50% along: fine, with nothing else to say.</summary>
    private static ProjectRiskRules.Facts Facts(
        bool open = true,
        bool floorApplies = true,
        decimal revenue = 10_000m,
        decimal profit = 5_000m,
        ProjectFinance.Projection? projected = null,
        int progressPct = 50,
        IReadOnlyList<ProjectRiskRules.Department>? departments = null,
        IReadOnlyList<DateOnly>? pending = null,
        IReadOnlyList<ProjectFinance.UnconvertedCost>? unconverted = null,
        LabourRules.Projection? runway = null,
        decimal? siteCashProfit = null,
        ProjectRiskRules.Settlement? settled = null)
        => new(open, floorApplies, revenue, profit, projected, progressPct, departments ?? [], pending ?? [], unconverted ?? [],
            runway, siteCashProfit, settled);

    private static IReadOnlyList<Risk> Risks(ProjectRiskRules.Facts facts) => ProjectRiskRules.Of(facts, Owner, Today);

    private static IEnumerable<ProjectRiskKind> Kinds(ProjectRiskRules.Facts facts) => Risks(facts).Select(r => r.Kind);

    private static LabourRules.Projection Runway(LabourRules.ProjectionState state, int? workingDays = null, DateOnly? zeroOn = null)
        => new(state, 500m, zeroOn, workingDays, null, null);

    private static Risk R(ProjectRiskKind kind, decimal weight) => new(kind, weight, null, "", "");

    private static (long ProjectId, IReadOnlyList<Risk> Risks) Project(long id, params Risk[] risks) => (id, risks);

    [Fact]
    public void A_project_in_good_standing_has_no_risks()
    {
        Risks(Facts()).ShouldBeEmpty();
    }

    [Fact]
    public void An_agreed_profit_of_exactly_zero_is_at_zero()
    {
        var risk = Risks(Facts(profit: 0m)).ShouldHaveSingleItem();

        risk.ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(ProjectRiskKind.ProfitAtZero),
            r => r.Amount.ShouldBe(0m),
            r => r.Weight.ShouldBe(0m),
            r => r.Fact.ShouldBe("An agreed profit of 0 USD on 10,000 USD agreed."),
            r => r.Rule.ShouldBe(ProjectRiskRules.ProfitAtZeroRule));
    }

    [Fact]
    public void A_loss_is_at_zero_and_weighs_what_was_lost()
    {
        var risk = Risks(Facts(profit: -250m)).ShouldHaveSingleItem();

        risk.ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(ProjectRiskKind.ProfitAtZero),
            r => r.Amount.ShouldBe(-250m),
            r => r.Weight.ShouldBe(250m),
            r => r.Fact.ShouldBe("An agreed profit of -250 USD on 10,000 USD agreed."));
    }

    [Fact]
    public void A_margin_of_exactly_10_percent_is_close_and_just_above_it_is_fine()
    {
        var risk = Risks(Facts(profit: 1_000m)).ShouldHaveSingleItem();

        risk.ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(ProjectRiskKind.ProfitClose),
            r => r.Amount.ShouldBe(1_000m),
            // the thinner the margin, the heavier
            r => r.Weight.ShouldBe(-0.1m),
            r => r.Fact.ShouldBe("An agreed profit of 1,000 USD on 10,000 USD agreed, a margin of 10%."),
            r => r.Rule.ShouldBe(ProjectRiskRules.ProfitCloseRule));

        Risks(Facts(profit: 1_000.01m)).ShouldBeEmpty();
    }

    [Fact]
    public void Without_the_floor_there_is_no_standing_and_no_projection_to_judge()
    {
        // internal work, or a client project with no agreed amount yet
        var facts = Facts(floorApplies: false, revenue: 0m, profit: -500m, projected: new ProjectFinance.Projection(2_000m, -2_000m, null));

        Risks(facts).ShouldBeEmpty();
    }

    [Fact]
    public void A_projection_to_a_profit_of_zero_or_below_is_a_projected_loss()
    {
        var loss = Risks(Facts(projected: new ProjectFinance.Projection(10_500m, -500m, -0.05m))).ShouldHaveSingleItem();

        loss.ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(ProjectRiskKind.ProjectedLoss),
            r => r.Amount.ShouldBe(-500m),
            r => r.Weight.ShouldBe(500m),
            r => r.Fact.ShouldBe("At 50% along, the cost so far points to 10,500 USD at completion: a profit of -500 USD on 10,000 USD agreed."),
            r => r.Rule.ShouldBe(ProjectRiskRules.ProjectedLossRule));

        Kinds(Facts(projected: new ProjectFinance.Projection(10_000m, 0m, 0m))).ShouldBe([ProjectRiskKind.ProjectedLoss]);
        Kinds(Facts(projected: new ProjectFinance.Projection(9_999m, 1m, 0.0001m))).ShouldBeEmpty();
    }

    [Fact]
    public void A_project_already_at_zero_is_not_also_projected_to_lose()
    {
        var facts = Facts(profit: -100m, projected: new ProjectFinance.Projection(12_000m, -2_000m, -0.2m));

        Kinds(facts).ShouldBe([ProjectRiskKind.ProfitAtZero]);
    }

    [Fact]
    public void A_close_project_can_also_be_projected_to_lose()
    {
        var facts = Facts(profit: 800m, projected: new ProjectFinance.Projection(10_200m, -200m, -0.02m));

        Kinds(facts).ShouldBe([ProjectRiskKind.ProjectedLoss, ProjectRiskKind.ProfitClose], ignoreOrder: true);
    }

    [Fact]
    public void A_site_that_spent_more_than_it_was_paid_is_short_whatever_else_is_known()
    {
        // no agreed amount yet, so no floor: the runway still counts
        var risk = Risks(Facts(floorApplies: false, revenue: 0m, profit: 0m, runway: Runway(LabourRules.ProjectionState.Losing),
            siteCashProfit: -1_200m)).ShouldHaveSingleItem();

        risk.ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(ProjectRiskKind.ShortRunway),
            r => r.Amount.ShouldBe(-1_200m),
            // ahead of any countdown: a countdown weighs minus its days
            r => r.Weight.ShouldBe(1_201m),
            r => r.Fact.ShouldBe("A cash profit of -1,200 USD: the site has spent more than the client paid."),
            r => r.Rule.ShouldBe(ProjectRiskRules.RunwayRule(Owner)));
    }

    [Fact]
    public void A_site_running_out_within_24_working_days_is_short_and_in_25_is_not()
    {
        var zeroOn = new DateOnly(2026, 12, 8);
        var risk = Risks(Facts(runway: Runway(LabourRules.ProjectionState.Ok, 24, zeroOn), siteCashProfit: 12_000m)).ShouldHaveSingleItem();

        risk.ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(ProjectRiskKind.ShortRunway),
            r => r.Amount.ShouldBe(12_000m),
            r => r.Weight.ShouldBe(-24m),
            r => r.Fact.ShouldBe("A cash profit of 12,000 USD runs out in 24 working days, on 8 Dec 2026, at 500 USD a day."),
            r => r.Rule.ShouldBe(ProjectRiskRules.RunwayRule(Owner)));

        Risks(Facts(runway: Runway(LabourRules.ProjectionState.Ok, 25, zeroOn.AddDays(1)), siteCashProfit: 12_500m)).ShouldBeEmpty();
    }

    [Fact]
    public void A_site_with_no_crew_to_project_with_or_done_is_never_short()
    {
        var soon = new DateOnly(2026, 11, 11);

        Risks(Facts(runway: Runway(LabourRules.ProjectionState.NoDefaults, 1, soon), siteCashProfit: 100m)).ShouldBeEmpty();
        Risks(Facts(runway: Runway(LabourRules.ProjectionState.Done, 1, soon), siteCashProfit: 100m)).ShouldBeEmpty();
    }

    [Fact]
    public void Refused_changes_still_waiting_weigh_the_days_since_the_oldest()
    {
        var several = Risks(Facts(pending: [new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 20)])).ShouldHaveSingleItem();

        several.ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(ProjectRiskKind.BlockedChange),
            r => r.Amount.ShouldBeNull(),
            r => r.Weight.ShouldBe(21m),
            r => r.Fact.ShouldBe("2 refused changes waiting, the oldest since 20 Oct 2026."),
            r => r.Rule.ShouldBe(ProjectRiskRules.BlockedChangeRule));

        var one = Risks(Facts(pending: [new DateOnly(2026, 11, 3)])).ShouldHaveSingleItem();
        one.Weight.ShouldBe(7m);
        one.Fact.ShouldBe("A refused change waiting since 3 Nov 2026.");
    }

    [Fact]
    public void A_department_is_over_budget_only_once_its_labour_cost_passes_it()
    {
        var risk = Risks(Facts(departments:
        [
            new ProjectRiskRules.Department("Architecture", 5_000m, 5_600m),
            new ProjectRiskRules.Department("Interior", 3_000m, 3_000m),
            new ProjectRiskRules.Department("Landscape", 2_000m, 1_500m),
        ])).ShouldHaveSingleItem();

        risk.ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(ProjectRiskKind.DepartmentOverBudget),
            r => r.Amount.ShouldBe(600m),
            r => r.Weight.ShouldBe(600m),
            r => r.Fact.ShouldBe("Architecture: 5,600 USD of labour on a budget of 5,000 USD, 600 USD over."),
            r => r.Rule.ShouldBe(ProjectRiskRules.DepartmentRule));
    }

    [Fact]
    public void Costs_with_no_exchange_rate_are_listed_in_their_own_currencies()
    {
        var risk = Risks(Facts(unconverted:
        [
            new ProjectFinance.UnconvertedCost(CostSource.Expenses, "EUR", 1_200m),
            new ProjectFinance.UnconvertedCost(CostSource.SiteExpense, "LBP", 9_000_000m),
            new ProjectFinance.UnconvertedCost(CostSource.Labour, "EUR", 300m),
        ])).ShouldHaveSingleItem();

        risk.ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(ProjectRiskKind.UnconvertedCosts),
            r => r.Amount.ShouldBeNull(),
            r => r.Weight.ShouldBe(3m),
            r => r.Fact.ShouldBe("Not counted: 1,200 EUR of expenses, 9,000,000 LBP of site expenses, 300 EUR of labour."),
            r => r.Rule.ShouldBe(ProjectRiskRules.UnconvertedRule));
    }

    [Fact]
    public void A_settlement_that_ended_at_zero_or_below_is_a_risk_with_the_profit_it_took()
    {
        var excused = new ProjectRiskRules.Settlement(OwedSettlement.Excused, 2_000m, new DateOnly(2026, 6, 15));
        var risk = Risks(Facts(open: false, profit: 0m, settled: excused)).ShouldHaveSingleItem();

        risk.ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(ProjectRiskKind.SettlementAtZero),
            r => r.Amount.ShouldBe(2_000m),
            r => r.Weight.ShouldBe(2_000m),
            r => r.Fact.ShouldBe("Excused at completion on 15 Jun 2026, taking 2,000 USD off the profit: an agreed profit of 0 USD."),
            r => r.Rule.ShouldBe(ProjectRiskRules.SettlementRule(Owner)));

        var writtenOff = new ProjectRiskRules.Settlement(OwedSettlement.WrittenOff, 3_500m, new DateOnly(2026, 9, 1));
        Risks(Facts(open: false, profit: -300m, settled: writtenOff)).ShouldHaveSingleItem().Fact
            .ShouldBe("Written off at completion on 1 Sep 2026, taking 3,500 USD off the profit: an agreed profit of -300 USD.");

        // still in profit after it: no risk
        Risks(Facts(open: false, profit: 1m, settled: excused)).ShouldBeEmpty();
    }

    [Fact]
    public void A_closed_project_gets_none_of_a_running_projects_risks()
    {
        var facts = Facts(
            open: false,
            profit: -500m,
            projected: new ProjectFinance.Projection(12_000m, -2_000m, -0.2m),
            departments: [new ProjectRiskRules.Department("Architecture", 1_000m, 4_000m)],
            pending: [new DateOnly(2026, 10, 1)],
            unconverted: [new ProjectFinance.UnconvertedCost(CostSource.Expenses, "EUR", 100m)],
            runway: Runway(LabourRules.ProjectionState.Losing),
            siteCashProfit: -900m);

        Risks(facts).ShouldBeEmpty();
    }

    [Fact]
    public void Projects_are_ranked_by_their_most_pressing_kind_then_its_weight_then_id_and_those_without_risks_dropped()
    {
        var ranked = ProjectRiskRules.Rank(
        [
            Project(5, R(ProjectRiskKind.BlockedChange, 10m)),
            Project(3, R(ProjectRiskKind.BlockedChange, 3m), R(ProjectRiskKind.ProfitClose, -0.05m)),
            Project(9, R(ProjectRiskKind.UnconvertedCosts, 1m), R(ProjectRiskKind.ProfitAtZero, 100m)),
            Project(2, R(ProjectRiskKind.ProfitAtZero, 500m)),
            Project(4),
            Project(1, R(ProjectRiskKind.ProfitAtZero, 100m)),
            Project(8, R(ProjectRiskKind.DepartmentOverBudget, 200m), R(ProjectRiskKind.DepartmentOverBudget, 900m)),
        ]);

        // the same kind and weight go by id
        ranked.Select(p => p.ProjectId).ShouldBe([2L, 1L, 9L, 3L, 5L, 8L]);

        // inside a project, the same order: kind, then weight
        ranked.Single(p => p.ProjectId == 9).Risks.Select(r => r.Kind).ShouldBe([ProjectRiskKind.ProfitAtZero, ProjectRiskKind.UnconvertedCosts]);
        ranked.Single(p => p.ProjectId == 3).Risks.Select(r => r.Kind).ShouldBe([ProjectRiskKind.ProfitClose, ProjectRiskKind.BlockedChange]);
        ranked.Single(p => p.ProjectId == 8).Risks.Select(r => r.Weight).ShouldBe([900m, 200m]);
    }

    [Fact]
    public void The_order_of_the_kinds_is_the_ranking()
    {
        Enum.GetValues<ProjectRiskKind>().ShouldBe([
            ProjectRiskKind.ProfitAtZero,
            ProjectRiskKind.ShortRunway,
            ProjectRiskKind.ProjectedLoss,
            ProjectRiskKind.ProfitClose,
            ProjectRiskKind.BlockedChange,
            ProjectRiskKind.DepartmentOverBudget,
            ProjectRiskKind.UnconvertedCosts,
            ProjectRiskKind.SettlementAtZero]);
    }
}
