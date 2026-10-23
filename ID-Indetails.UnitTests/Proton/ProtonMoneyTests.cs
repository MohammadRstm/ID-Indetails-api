using System.Text.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Tools;
using Shouldly;

namespace ID_Indetails.UnitTests.Proton;

public class ProtonMoneyTests
{
    private static JsonElement Details(object payload) => JsonSerializer.SerializeToElement(payload);

    private static FinanceProjectResponse Project(string title, decimal? margin) => new(
        title.Length, title, null, ProjectStatus.Active, margin is null ? BillingType.Internal : BillingType.Client, 1000, 0, 10,
        new ProjectCostResponse(0, 0, 0, 0, 0, [], 0), 0, margin, margin is not null, null, 0);

    /// <summary>"Lowest margins" means the projects that have one: internal and too-early projects never fill the list.</summary>
    [Fact]
    public void Projects_without_a_margin_rank_last_both_ways()
    {
        FinanceProjectResponse[] projects = [Project("Internal", null), Project("Thin", 0.05m), Project("Early", null), Project("Fat", 0.40m)];

        GetFinanceProjectsTool.Rank(projects, FinanceProjectSort.Margin, ascending: true).Select(p => p.Title)
            .ShouldBe(["Thin", "Fat", "Internal", "Early"]);
        GetFinanceProjectsTool.Rank(projects, FinanceProjectSort.Margin, ascending: false).Select(p => p.Title)
            .ShouldBe(["Fat", "Thin", "Internal", "Early"]);
    }

    [Fact]
    public void An_unconverted_cost_says_its_currency_and_source()
        => ProtonAnalysis.Unconverted([new UnconvertedCostResponse(CostSource.GroundLabour, "LBP", 4_500_000m), new UnconvertedCostResponse(CostSource.Expenses, "LBP", 12.5m)])
            .ShouldBe(["4500000.00 LBP (ground labour)", "12.50 LBP (expenses)"]);

    /// <summary>The feed keeps an amount without its unit; Proton is told it, so 89,500 LBP per USD isn't read as 89,500 USD.</summary>
    [Fact]
    public void An_activity_amount_comes_with_its_unit()
    {
        ActivityRow.UnitOf(ActivityEventKinds.ExchangeRateSet, 89_500m, Details(new { currency = "LBP", effectiveFrom = "2026-10-01" }))
            .ShouldBe("LBP per USD");
        ActivityRow.UnitOf(ActivityEventKinds.ProgressSet, 40m, null).ShouldBe("percent");
        ActivityRow.UnitOf(ActivityEventKinds.ExpenseAdded, 4_500_000m, Details(new { label = "Taxi", category = "travel", currency = "LBP" })).ShouldBe("LBP");
        ActivityRow.UnitOf(ActivityEventKinds.PaymentRecorded, 1_000m, Details(new { method = "bank" })).ShouldBe("USD");
        ActivityRow.UnitOf(ActivityEventKinds.ProjectCreated, null, null).ShouldBeNull();
    }
}
