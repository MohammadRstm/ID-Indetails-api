using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Finance.Intelligence;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using Levers = ID_Indetails.api.Business.Finance.Intelligence.ProjectScenarioRules.Levers;

namespace ID_Indetails.UnitTests.Business;

public class ProjectScenarioRulesTests
{
    /// <summary>
    /// 100,000 of work, 2,000 of permits, 10% supervision and 11% TVA: 112,000 agreed (work + 10,000 supervision + permits),
    /// 123,000 for the client (11,000 of TVA on the work only).
    /// </summary>
    private static ProjectScenarioRules.Facts Site(decimal cost = 50_000m, decimal received = 60_000m, decimal pocketNet = 0)
        => new(DomainCodes.Execution, BillingType: BillingType.Client, Revenue: 112_000m, Work: 100_000m, Permits: 2_000m, SupervisionRate: 0.10m,
            TvaRate: 0.11m, Untaxed: 0, Cost: cost, PocketNet: pocketNet, Received: received);

    private static ProjectScenarioRules.Facts Design(
        decimal revenue = 10_000m, decimal cost = 6_000m, decimal received = 0, decimal tvaRate = 0.11m, decimal untaxed = 0)
        => new(DomainCodes.Design, BillingType: BillingType.Client, Revenue: revenue, Work: 0, Permits: 0, SupervisionRate: 0,
            TvaRate: tvaRate, Untaxed: untaxed, Cost: cost, PocketNet: 0, Received: received);

    private static ProjectScenarioRules.Facts Internal(decimal cost = 1_000m)
        => new(DomainCodes.Design, BillingType: BillingType.Internal, Revenue: 0, Work: 0, Permits: 0, SupervisionRate: 0,
            TvaRate: 0.11m, Untaxed: 0, Cost: cost, PocketNet: 0, Received: 0);

    [Fact]
    public void Before_is_the_project_as_it_stands()
    {
        var before = ProjectScenarioRules.Run(Site(), new Levers()).Before;

        before.ShouldSatisfyAllConditions(
            b => b.Revenue.ShouldBe(112_000m),
            b => b.RevenueWithTva.ShouldBe(123_000m),
            b => b.Cost.ShouldBe(50_000m),
            b => b.Profit.ShouldBe(62_000m),
            b => b.Margin.ShouldBe(0.5536m),
            b => b.Owed.ShouldBe(63_000m),
            b => b.Standing.ShouldBe(ProfitFloorRules.Standing.Fine));
    }

    [Fact]
    public void A_vo_on_a_site_raises_the_work_its_supervision_and_the_tva_on_it()
    {
        var outcome = ProjectScenarioRules.Run(Site(), new Levers(VoAmount: 10_000m));

        // 110,000 of work + 11,000 supervision + 2,000 permits; 12,100 of TVA on the work
        outcome.After.ShouldSatisfyAllConditions(
            a => a.Revenue.ShouldBe(123_000m),
            a => a.RevenueWithTva.ShouldBe(135_100m),
            a => a.Cost.ShouldBe(50_000m),
            a => a.Profit.ShouldBe(73_000m),
            a => a.Margin.ShouldBe(0.5935m),
            a => a.Owed.ShouldBe(75_100m),
            a => a.Standing.ShouldBe(ProfitFloorRules.Standing.Fine));
        (outcome.After.Revenue - outcome.Before.Revenue).ShouldBe(11_000m);
        (outcome.After.RevenueWithTva - outcome.Before.RevenueWithTva).ShouldBe(12_100m);
        outcome.FloorWouldRefuse.ShouldBeFalse();
        outcome.SettlementImpossible.ShouldBeFalse();
    }

    [Fact]
    public void A_vo_on_design_moves_the_price_and_its_tva()
    {
        var after = ProjectScenarioRules.Run(Design(), new Levers(VoAmount: 1_000m)).After;

        after.Revenue.ShouldBe(11_000m);
        after.RevenueWithTva.ShouldBe(12_210m);
        after.Profit.ShouldBe(5_000m);
    }

    [Fact]
    public void An_extra_cost_lowers_profit_and_nothing_else()
    {
        var outcome = ProjectScenarioRules.Run(Site(), new Levers(ExtraCost: 5_000m));

        outcome.After.ShouldSatisfyAllConditions(
            a => a.Revenue.ShouldBe(outcome.Before.Revenue),
            a => a.RevenueWithTva.ShouldBe(outcome.Before.RevenueWithTva),
            a => a.Owed.ShouldBe(outcome.Before.Owed),
            a => a.Cost.ShouldBe(55_000m),
            a => a.Profit.ShouldBe(57_000m),
            a => a.Margin.ShouldBe(0.5089m));
    }

    [Fact]
    public void A_supervision_rate_moves_revenue_and_never_the_tva()
    {
        var outcome = ProjectScenarioRules.Run(Site(), new Levers(SupervisionRate: 0.15m));

        // 15,000 supervision instead of 10,000; TVA stays 11,000, on the work alone
        outcome.After.ShouldSatisfyAllConditions(
            a => a.Revenue.ShouldBe(117_000m),
            a => a.RevenueWithTva.ShouldBe(128_000m),
            a => a.Profit.ShouldBe(67_000m),
            a => a.Owed.ShouldBe(68_000m));
    }

    [Fact]
    public void Tva_moves_what_the_client_pays_and_owes_never_revenue_or_profit()
    {
        var site = ProjectScenarioRules.Run(Site(), new Levers(TvaRate: 0.05m));

        site.After.ShouldSatisfyAllConditions(
            a => a.RevenueWithTva.ShouldBe(117_000m),
            a => a.Owed.ShouldBe(57_000m),
            a => a.Revenue.ShouldBe(site.Before.Revenue),
            a => a.Profit.ShouldBe(site.Before.Profit),
            a => a.Margin.ShouldBe(site.Before.Margin));

        var design = ProjectScenarioRules.Run(Design(), new Levers(TvaRate: 0.20m));

        design.Before.RevenueWithTva.ShouldBe(11_100m);
        design.After.RevenueWithTva.ShouldBe(12_000m);
        design.After.Revenue.ShouldBe(10_000m);
        design.After.Profit.ShouldBe(design.Before.Profit);
    }

    [Fact]
    public void Settling_a_design_project_lowers_its_price_to_the_net_of_what_was_received()
    {
        // 9,000 taxed at 11% and 1,000 of a VO without TVA: 10,990 for the client, who paid 5,550
        var outcome = ProjectScenarioRules.Run(Design(revenue: 10_000m, cost: 2_000m, received: 5_550m, untaxed: 1_000m), new Levers(Settle: true));

        outcome.Before.Owed.ShouldBe(5_440m);
        outcome.After.ShouldSatisfyAllConditions(
            a => a.Revenue.ShouldBe(Tva.NetFor(5_550m, 0.11m, 1_000m)),
            a => a.Revenue.ShouldBe(5_099.10m),
            a => a.RevenueWithTva.ShouldBe(5_550m),
            a => a.Owed.ShouldBe(0m),
            a => a.Profit.ShouldBe(3_099.10m));
        outcome.FloorWouldRefuse.ShouldBeFalse();
        outcome.SettlementImpossible.ShouldBeFalse();
    }

    [Fact]
    public void Settling_a_site_finds_the_work_price_whose_client_total_is_what_was_received()
    {
        // 50,000 of work + 5,000 supervision + 2,000 permits + 5,500 TVA = 62,500
        ExecutionPricing.WorkFor(62_500m, 2_000m, 0.10m, 0.11m).ShouldBe(50_000m);

        var outcome = ProjectScenarioRules.Run(Site(received: 62_500m), new Levers(Settle: true));

        outcome.Before.Owed.ShouldBe(60_500m);
        outcome.After.ShouldSatisfyAllConditions(
            a => a.Revenue.ShouldBe(57_000m),
            a => a.RevenueWithTva.ShouldBe(62_500m),
            a => a.Owed.ShouldBe(0m),
            a => a.Profit.ShouldBe(7_000m));
        outcome.SettlementImpossible.ShouldBeFalse();
        outcome.FloorWouldRefuse.ShouldBeFalse();
    }

    [Fact]
    public void A_site_whose_client_paid_less_than_its_permits_cannot_be_settled()
    {
        var settled = ProjectScenarioRules.Run(Site(received: 1_500m), new Levers(Settle: true));

        settled.SettlementImpossible.ShouldBeTrue();
        settled.After.Revenue.ShouldBe(112_000m);

        // only a settlement is impossible: anything else is priced as usual
        ProjectScenarioRules.Run(Site(received: 1_500m), new Levers(VoAmount: 1_000m)).SettlementImpossible.ShouldBeFalse();
    }

    [Fact]
    public void The_floor_would_refuse_a_cost_or_a_vo_that_takes_profit_to_zero_or_below()
    {
        var cost = ProjectScenarioRules.Run(Design(), new Levers(ExtraCost: 5_000m));
        cost.FloorWouldRefuse.ShouldBeTrue();
        cost.After.Profit.ShouldBe(-1_000m);
        cost.After.Standing.ShouldBe(ProfitFloorRules.Standing.AtZero);

        // zero is refused too
        var vo = ProjectScenarioRules.Run(Design(), new Levers(VoAmount: -4_000m));
        vo.After.Profit.ShouldBe(0m);
        vo.FloorWouldRefuse.ShouldBeTrue();

        ProjectScenarioRules.Run(Design(), new Levers(DelayCost: 4_000m)).FloorWouldRefuse.ShouldBeTrue();
        ProjectScenarioRules.Run(Site(), new Levers(ExtraCost: 62_000m)).FloorWouldRefuse.ShouldBeTrue();
        ProjectScenarioRules.Run(Site(), new Levers(ExtraCost: 61_999.99m)).FloorWouldRefuse.ShouldBeFalse();
    }

    [Fact]
    public void A_settlement_alone_is_never_held_to_the_floor()
    {
        // 10,000 untaxed, 6,000 spent, 5,000 paid: settling leaves a 1,000 loss
        var settled = ProjectScenarioRules.Run(Design(received: 5_000m, tvaRate: 0m), new Levers(Settle: true));

        settled.After.Revenue.ShouldBe(5_000m);
        settled.After.Profit.ShouldBe(-1_000m);
        settled.After.Standing.ShouldBe(ProfitFloorRules.Standing.AtZero);
        settled.FloorWouldRefuse.ShouldBeFalse();

        // the other changes are held to it, settlement or not
        ProjectScenarioRules.Run(Design(received: 5_000m, tvaRate: 0m), new Levers(ExtraCost: 5_000m, Settle: true))
            .FloorWouldRefuse.ShouldBeTrue();
    }

    [Fact]
    public void A_change_that_raises_profit_is_never_refused_even_below_zero()
    {
        var outcome = ProjectScenarioRules.Run(Design(cost: 11_000m), new Levers(VoAmount: 500m));

        outcome.Before.Standing.ShouldBe(ProfitFloorRules.Standing.AtZero);
        outcome.After.Profit.ShouldBe(-500m);
        outcome.FloorWouldRefuse.ShouldBeFalse();
    }

    [Fact]
    public void Internal_work_ignores_price_levers_and_has_no_standing()
    {
        var outcome = ProjectScenarioRules.Run(Internal(),
            new Levers(VoAmount: 5_000m, ExtraCost: 5_000m, SupervisionRate: 0.20m, TvaRate: 0.20m, Settle: true));

        outcome.Before.Standing.ShouldBeNull();
        outcome.After.ShouldSatisfyAllConditions(
            a => a.Revenue.ShouldBe(0m),
            a => a.RevenueWithTva.ShouldBe(0m),
            a => a.Owed.ShouldBe(0m),
            a => a.Cost.ShouldBe(6_000m),
            a => a.Profit.ShouldBe(-6_000m),
            a => a.Margin.ShouldBeNull(),
            a => a.Standing.ShouldBeNull());
        outcome.FloorWouldRefuse.ShouldBeFalse();
        outcome.SettlementImpossible.ShouldBeFalse();
    }

    [Fact]
    public void Standing_is_fine_then_close_at_a_ten_percent_margin_then_at_zero()
    {
        var facts = Design(cost: 8_000m);

        ProjectScenarioRules.Run(facts, new Levers()).Before.Standing.ShouldBe(ProfitFloorRules.Standing.Fine);
        ProjectScenarioRules.Run(facts, new Levers(ExtraCost: 1_000m)).After.Standing.ShouldBe(ProfitFloorRules.Standing.Close);
        ProjectScenarioRules.Run(facts, new Levers(ExtraCost: 2_000m)).After.Standing.ShouldBe(ProfitFloorRules.Standing.AtZero);
    }

    [Fact]
    public void A_site_with_no_work_price_yet_is_outside_the_floor_until_a_vo_gives_it_one()
    {
        var unpriced = new ProjectScenarioRules.Facts(DomainCodes.Execution, BillingType.Client, Revenue: 0, Work: 0, Permits: 0,
            SupervisionRate: 0.10m, TvaRate: 0.11m, Untaxed: 0, Cost: 100m, PocketNet: 0, Received: 0);

        var outcome = ProjectScenarioRules.Run(unpriced, new Levers(VoAmount: 10_000m));

        outcome.Before.Standing.ShouldBeNull();
        outcome.After.Revenue.ShouldBe(11_000m);
        outcome.After.Standing.ShouldBe(ProfitFloorRules.Standing.Fine);
        outcome.FloorWouldRefuse.ShouldBeFalse();
    }

    [Fact]
    public void A_delay_adds_its_cost()
    {
        // 3 weeks at 1,500
        var outcome = ProjectScenarioRules.Run(Site(), new Levers(DelayCost: 4_500m));

        outcome.After.Cost.ShouldBe(54_500m);
        outcome.After.Profit.ShouldBe(57_500m);
        outcome.After.Revenue.ShouldBe(outcome.Before.Revenue);
    }

    [Fact]
    public void Profit_counts_the_pocket_money()
    {
        // 5,000 moved to the pocket, 3,000 paid back by it
        var outcome = ProjectScenarioRules.Run(Site(pocketNet: -2_000m), new Levers(ExtraCost: 1_000m));

        outcome.Before.Profit.ShouldBe(60_000m);
        outcome.After.Profit.ShouldBe(59_000m);
    }
}
