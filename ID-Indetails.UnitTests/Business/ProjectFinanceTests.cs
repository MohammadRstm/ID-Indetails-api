using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Enums;
using Shouldly;
using static ID_Indetails.api.Business.Finance.ProjectFinance;

namespace ID_Indetails.UnitTests.Business;

public class ProjectFinanceTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Mar1 = new(2026, 3, 1);

    // 89,500 LBP to the dollar from 1 January, 90,000 from 1 March.
    private static readonly ExchangeRateTable Rates = new([
        new ExchangeRateTable.Rate("LBP", Jan1, 89_500m),
        new ExchangeRateTable.Rate("LBP", Mar1, 90_000m),
    ]);

    private static Costs CostOf(decimal labour = 0, decimal expenses = 0, decimal materials = 0, decimal ground = 0)
        => new(labour, expenses, materials, ground, []);

    [Fact]
    public void Amounts_convert_at_the_rate_in_force_on_their_day()
    {
        Rates.ToUsd(179_000m, "LBP", new DateOnly(2026, 2, 15)).ShouldBe(2m);
        Rates.ToUsd(180_000m, "LBP", Mar1).ShouldBe(2m);
        Rates.ToUsd(50m, "USD", new DateOnly(2020, 1, 1)).ShouldBe(50m);
        Rates.ToUsd(179_000m, "LBP", new DateOnly(2025, 12, 31)).ShouldBeNull();
    }

    [Fact]
    public void Costs_are_converted_by_day_and_unconvertible_ones_are_listed_not_added()
    {
        var costs = ToUsd([
            new CostItem(CostSource.Expenses, "USD", 100m, Mar1),
            new CostItem(CostSource.Expenses, "LBP", 900_000m, Mar1),
            new CostItem(CostSource.Materials, "LBP", 179_000m, new DateOnly(2025, 6, 1)),
            new CostItem(CostSource.GroundLabour, "USD", 240m, Mar1),
        ], Rates);

        costs.Expenses.ShouldBe(110m);
        costs.Materials.ShouldBe(0m);
        costs.GroundLabour.ShouldBe(240m);
        costs.Total.ShouldBe(350m);
        costs.Unconverted.ShouldHaveSingleItem().ShouldBe(new UnconvertedCost(CostSource.Materials, "LBP", 179_000m));
    }

    [Fact]
    public void Labour_uses_the_hourly_cost_in_force_on_the_day_worked()
    {
        var (items, unpriced) = PriceLabour(
            [
                new WorkedTime(1, new DateOnly(2026, 2, 10), 120),
                new WorkedTime(1, new DateOnly(2026, 3, 10), 60),
                new WorkedTime(1, new DateOnly(2025, 12, 1), 60),
                new WorkedTime(2, Mar1, 90),
            ],
            [
                new PayRate(1, Jan1, 10m, "USD"),
                new PayRate(1, Mar1, 20m, "USD"),
            ]);

        // Time before the first pay record uses the earliest rate; someone with no pay can't be priced.
        items.Select(i => i.Amount).ShouldBe([20m, 20m, 10m]);
        unpriced.ShouldBe(90);
    }

    [Fact]
    public void Profit_and_margin_come_from_revenue_and_cost()
    {
        var summary = Summarise(new Facts(IsClientWork: true, IsDone: true, ProgressPct: 100, Revenue: 10_000m, LabourMinutes: 600), CostOf(labour: 4000m, expenses: 1000m));

        summary.Cost.ShouldBe(5000m);
        summary.Profit.ShouldBe(5000m);
        summary.Margin.ShouldBe(0.5m);
        summary.RecognisedToDate.ShouldBe(10_000m);
        summary.CostPerHour.ShouldBe(500m);
        summary.Projected.ShouldBeNull();
    }

    [Fact]
    public void Margin_is_withheld_until_the_project_is_done_5_percent_along_or_has_10_hours()
    {
        var early = new Facts(true, false, 4, 10_000m, 9 * 60);
        Summarise(early, CostOf(labour: 100m)).Margin.ShouldBeNull();
        Summarise(early with { ProgressPct = 5 }, CostOf(labour: 100m)).Margin.ShouldBe(0.99m);
        Summarise(early with { LabourMinutes = 10 * 60 }, CostOf(labour: 100m)).MarginKnown.ShouldBeTrue();
    }

    [Fact]
    public void An_open_project_projects_its_cost_from_progress_and_recognises_revenue_by_progress()
    {
        var summary = Summarise(new Facts(true, false, 25, 10_000m, 0), CostOf(materials: 2000m));

        summary.Projected.ShouldBe(new Projection(8000m, 2000m, 0.2m));
        summary.RecognisedToDate.ShouldBe(2500m);
    }

    [Fact]
    public void Internal_projects_have_no_revenue_and_no_margin()
    {
        var summary = Summarise(new Facts(false, true, 100, 5000m, 60), CostOf(labour: 300m));

        summary.Revenue.ShouldBe(0m);
        summary.Profit.ShouldBe(-300m);
        summary.Margin.ShouldBeNull();
        summary.Projected.ShouldBeNull();
    }

    [Fact]
    public void Material_charge_converts_each_client_price_and_counts_what_it_cannot()
    {
        var (charge, unconverted) = MaterialCharge(
            [
                (new ExecutionPricing.Material(100m, null), "USD", Mar1),
                (new ExecutionPricing.Material(900_000m, null), "LBP", Mar1),
                (new ExecutionPricing.Material(50m, 80m), "USD", Mar1),
                (new ExecutionPricing.Material(179_000m, null), "LBP", new DateOnly(2025, 1, 1)),
            ],
            markup: 0.10m,
            Rates);

        // 110 + (990,000 LBP = 11) + 80 (manual price)
        charge.ShouldBe(201m);
        unconverted.ShouldBe(1);
    }
}
