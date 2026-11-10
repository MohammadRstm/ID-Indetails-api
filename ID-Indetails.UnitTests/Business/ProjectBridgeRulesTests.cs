using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Finance.Intelligence;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ProjectBridgeRulesTests
{
    private static ProjectFinance.Costs Costs(decimal labour = 0, decimal expenses = 0, decimal siteExpenses = 0, decimal groundLabour = 0)
        => new(labour, expenses, siteExpenses, groundLabour, []);

    private static IEnumerable<(BridgeStepKind Kind, decimal Amount)> Bridge(ProjectBridgeRules.Facts facts)
    {
        var steps = ProjectBridgeRules.Of(facts);
        // whatever the project, the steps add up to its agreed profit
        steps.Sum(s => s.Amount).ShouldBe(facts.Revenue - facts.Costs.Total + facts.PocketNet);
        return steps.Select(s => (s.Kind, s.Amount));
    }

    [Fact]
    public void A_design_project_goes_from_its_payment_through_its_vos_and_a_settlement_to_its_costs()
    {
        // 10,000 agreed, 2,500 of VOs, 500 excused at completion; supervision and permits are a site's, never Design's
        var facts = new ProjectBridgeRules.Facts(DomainCodes.Design, IsClientWork: true, Revenue: 12_000m, VariationOrders: 2_500m,
            Settlement: -500m, PaidAhead: 0, Supervision: 300m, Permits: 200m, Costs(labour: 4_000m, expenses: 500m), PocketNet: 0);

        Bridge(facts).ShouldBe([
            (BridgeStepKind.BasePrice, 10_000m),
            (BridgeStepKind.VariationOrders, 2_500m),
            (BridgeStepKind.Settlement, -500m),
            (BridgeStepKind.Labour, -4_000m),
            (BridgeStepKind.Expenses, -500m)]);
    }

    [Fact]
    public void A_site_goes_from_its_client_boq_through_its_vos_supervision_permits_costs_and_pocket_money()
    {
        // 100,000 of client BOQ, 10,000 of VOs, a VO of -20,000 settling what was owed and one of 5,000 keeping an
        // overpayment: 95,000 of work, 9,500 supervision and 2,000 permits. 5,000 moved to the pocket, 2,000 paid by it.
        var facts = new ProjectBridgeRules.Facts(DomainCodes.Execution, IsClientWork: true, Revenue: 106_500m, VariationOrders: 10_000m,
            Settlement: -20_000m, PaidAhead: 5_000m, Supervision: 9_500m, Permits: 2_000m,
            Costs(siteExpenses: 40_000m, groundLabour: 15_000m), PocketNet: -3_000m);

        Bridge(facts).ShouldBe([
            (BridgeStepKind.BasePrice, 100_000m),
            (BridgeStepKind.VariationOrders, 10_000m),
            (BridgeStepKind.Settlement, -20_000m),
            (BridgeStepKind.PaidAhead, 5_000m),
            (BridgeStepKind.Supervision, 9_500m),
            (BridgeStepKind.Permits, 2_000m),
            (BridgeStepKind.SiteExpenses, -40_000m),
            (BridgeStepKind.GroundLabour, -15_000m),
            (BridgeStepKind.Pocket, -3_000m)]);
    }

    [Fact]
    public void Internal_work_is_its_costs_alone()
    {
        var facts = new ProjectBridgeRules.Facts(DomainCodes.Design, IsClientWork: false, Revenue: 0, VariationOrders: 0,
            Settlement: 0, PaidAhead: 0, Supervision: 0, Permits: 0, Costs(labour: 3_000m, expenses: 200m), PocketNet: 0);

        Bridge(facts).ShouldBe([(BridgeStepKind.Labour, -3_000m), (BridgeStepKind.Expenses, -200m)]);
    }

    [Fact]
    public void Steps_of_nothing_are_left_out()
    {
        var facts = new ProjectBridgeRules.Facts(DomainCodes.Design, IsClientWork: true, Revenue: 10_000m, VariationOrders: 0,
            Settlement: 0, PaidAhead: 0, Supervision: 0, Permits: 0, Costs(expenses: 750m), PocketNet: 0);

        Bridge(facts).ShouldBe([(BridgeStepKind.BasePrice, 10_000m), (BridgeStepKind.Expenses, -750m)]);
    }

    [Fact]
    public void The_base_price_is_what_is_left_of_the_revenue_once_the_rest_is_accounted_for()
    {
        // a site priced before BOQs: no client BOQ to read, so its base price is the agreed amount less supervision and permits
        var site = new ProjectBridgeRules.Facts(DomainCodes.Execution, IsClientWork: true, Revenue: 13_100m, VariationOrders: 0,
            Settlement: 0, PaidAhead: 0, Supervision: 1_100m, Permits: 2_000m, Costs(), PocketNet: 0);

        Bridge(site).ShouldBe([(BridgeStepKind.BasePrice, 10_000m), (BridgeStepKind.Supervision, 1_100m), (BridgeStepKind.Permits, 2_000m)]);

        // a client project with nothing agreed yet still starts at its base price, of nothing
        var unpriced = new ProjectBridgeRules.Facts(DomainCodes.Design, IsClientWork: true, Revenue: 0, VariationOrders: 0,
            Settlement: 0, PaidAhead: 0, Supervision: 0, Permits: 0, Costs(labour: 100m), PocketNet: 0);

        Bridge(unpriced).ShouldBe([(BridgeStepKind.BasePrice, 0m), (BridgeStepKind.Labour, -100m)]);
    }
}
