using System.ComponentModel;
using System.Globalization;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Insights;
using ID_Indetails.api.DTO.Response.Insights;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Insights.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

/// <param name="Amount">USD, net of TVA: positive adds to the agreed profit, negative takes from it.</param>
public sealed record BridgeRow(BridgeStepKind Step, decimal Amount);

/// <summary>A Design department's part of a project and the labour logged in it. It never lists the people in it.</summary>
/// <param name="Id">Null for the hours of people in no department, or in one no longer on the project.</param>
/// <param name="SharePercent">Its part of the price; null for those other hours.</param>
/// <param name="LabourSharePercent">Its share of the project's labour cost, while there is any.</param>
/// <param name="UnconvertedLabour">Labour with no exchange rate on its day, per currency: not in the cost or what is left.</param>
public sealed record InsightDepartmentRow(
    long? Id,
    string Name,
    decimal? SharePercent,
    decimal Budget,
    decimal Cost,
    decimal Left,
    int MinutesLogged,
    decimal? LabourSharePercent,
    IReadOnlyList<CurrencyAmountResponse>? UnconvertedLabour);

/// <summary>One project's profitability, all time, as GET /api/finance/insights/projects/{id} returns it.</summary>
/// <param name="AgreedRevenue">The contract value, net of TVA, not cash.</param>
/// <param name="AgreedWithTva">What the client is agreed to pay, TVA included.</param>
/// <param name="AgreedProfit">Agreed revenue less all the cost so far, with a site's pocket money.</param>
/// <param name="AgreedMarginPct">Agreed profit as a whole percentage of the agreed revenue; empty until it means something.</param>
/// <param name="Standing">Against the profit floor; empty where the floor doesn't apply (internal work, no agreed amount yet).</param>
/// <param name="Received">What the client paid, TVA included; <paramref name="ReceivedTva"/> is the TVA in it.</param>
/// <param name="Owed">What the client still owes, TVA included.</param>
/// <param name="SiteCashProfit">Execution: received net of TVA, less cost, less what was moved to the pocket money, plus what it paid in.</param>
/// <param name="Bridge">From the base price to the agreed profit, in order; the steps add up to it.</param>
/// <param name="Departments">Design only.</param>
public sealed record ProjectFinancialSummary(
    long ProjectId,
    string Title,
    string Domain,
    ProjectStatus Status,
    BillingType BillingType,
    decimal AgreedRevenue,
    decimal AgreedWithTva,
    decimal TvaPct,
    decimal? SupervisionPct,
    ProjectCostResponse Cost,
    decimal AgreedProfit,
    int? AgreedMarginPct,
    ProfitStanding? Standing,
    int ProgressPct,
    decimal RecognisedToDate,
    ProtonProjection? Projected,
    decimal Received,
    decimal ReceivedTva,
    decimal Owed,
    decimal? SiteCashProfit,
    decimal? ToPocket,
    decimal? FromPocket,
    IReadOnlyList<BridgeRow> Bridge,
    IReadOnlyList<InsightDepartmentRow>? Departments,
    string Note);

/// <summary>The words every Financial Intelligence tool uses for the same thing.</summary>
internal static class ProtonInsights
{
    public static string StandingRule { get; } =
        $"standing is against the profit floor: at_zero when the agreed profit is 0 or below, close at a margin of " +
        $"{(ProfitFloorRules.WarnMargin * 100).ToString("0", CultureInfo.InvariantCulture)}% or less, fine above it; empty where the floor doesn't apply";

    public static string Usd(decimal amount) => amount.ToString("#,0.##", CultureInfo.InvariantCulture) + " USD";

    public static ProtonBasis Basis(string label, long projectId, InsightBasisResponse basis, params string[] rules)
        => new(label, null, null, false, null, $"project #{projectId}, all time", [basis.Method, .. basis.Assumptions, .. rules], basis.LeftOut);

    public static string StepName(BridgeStepKind kind) => kind switch
    {
        BridgeStepKind.BasePrice => "Base price",
        BridgeStepKind.VariationOrders => "Variation orders",
        BridgeStepKind.Settlement => "Excused or written off at completion",
        BridgeStepKind.PaidAhead => "Overpayment kept",
        BridgeStepKind.Supervision => "Supervision",
        BridgeStepKind.Permits => "Permits",
        BridgeStepKind.Labour => "Labour",
        BridgeStepKind.Expenses => "Expenses",
        BridgeStepKind.SiteExpenses => "Site expenses",
        BridgeStepKind.GroundLabour => "Ground labour",
        _ => "Pocket money (paid in less moved out)",
    };
}

/// <summary>
/// One project's profitability: agreed revenue, cost by source, agreed profit and its standing against the profit
/// floor, cash and pocket money, where the profit comes from (the bridge) and Design's departments, never their people.
/// </summary>
public sealed class GetProjectFinancialSummaryTool(IFinanceInsightService insights) : ProtonTool<ProjectIdInput>
{
    public override string Name => "get_project_financial_summary";

    public override string Description =>
        "One project's profitability, all time, in USD: agreed revenue (net of TVA) and with TVA, cost by source, agreed profit " +
        "and margin, its standing against the profit floor (fine, close or at_zero), progress, recognised to date and projection; " +
        "what the client paid and still owes (TVA included); on a site its site cash profit and the pocket money moved out and " +
        "paid in; the bridge from the base price to the agreed profit (variation orders, a settlement, supervision, permits, " +
        "each cost, pocket money); and on a Design project each department's share, budget, cost, minutes and share of the " +
        "labour, never its people. Use it for \"how profitable is it\", \"where does its profit come from\", \"how close is it to the floor\".";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<FinanceInsightsController>(nameof(FinanceInsightsController.Project));

    protected override async Task<ProtonToolOutput> RunAsync(ProjectIdInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var p = await insights.ProjectAsync(input.ProjectId, cancellationToken);
        var site = p.Domain == DomainCodes.Execution;
        var summary = new ProjectFinancialSummary(
            p.ProjectId, p.Title, p.Domain, p.Status, p.BillingType,
            p.AgreedRevenue, p.AgreedWithTva, p.TvaRate * 100, site ? p.SupervisionRate * 100 : null,
            p.Cost, p.AgreedProfit, ProtonAnalysis.Pct(p.Margin), p.Standing,
            p.ProgressPct, p.RecognisedToDate, ProtonProjection.Of(p.Projected),
            p.Received, p.ReceivedTva, p.Owed,
            site ? p.SiteCashProfit : null, site ? p.ToPocket : null, site ? p.FromPocket : null,
            p.Bridge.Select(s => new BridgeRow(s.Kind, s.Amount)).ToList(),
            p.Departments?.Select(d => new InsightDepartmentRow(
                d.Id, d.Name, d.SharePercent, d.Budget, d.Cost, d.Left, d.Minutes, d.LabourSharePercent,
                d.Unconverted.Count > 0 ? d.Unconverted : null)).ToList(),
            "All time, in USD. Agreed revenue, cost, agreed_profit and the bridge are net of TVA; agreed_with_tva, received " +
            "and owed include it. The bridge's steps add up to agreed_profit. Departments carry no people: never work out " +
            "one person's pay or hourly cost from them.");
        return new ProtonToolOutput(summary, [new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, p.Domain)])
        {
            Basis = ProtonInsights.Basis($"Financial summary of {p.Title}", p.ProjectId, p.Basis, ProtonInsights.StandingRule),
            Datasets = Datasets(p),
        };
    }

    /// <summary>Its headline figures, and the bridge in order (a table keeps the order; a bar ranks the steps).</summary>
    private static IReadOnlyList<ProtonDataset> Datasets(ProjectInsightResponse p)
    {
        var usd = (string key, string label) => ProtonDatasets.Figure(key, label, ProtonUnit.Usd);
        var site = p.Domain == DomainCodes.Execution;
        var notes = new List<string>
        {
            site
                ? "Agreed revenue, cost and profit are net of TVA; received includes it. Site cash profit: received net of TVA, less cost and pocket money moved out, plus pocket money paid in."
                : "Agreed revenue, cost and profit are net of TVA; received and owed include it.",
        };
        if (p.Cost.Unconverted.Count > 0)
        {
            notes.Add($"Left out of these USD figures for want of an exchange rate on their day: {string.Join(", ", ProtonAnalysis.Unconverted(p.Cost.Unconverted))}.");
        }

        if (p.Cost.UnpricedLabourMinutes > 0)
        {
            notes.Add($"{ProtonText.Hours(p.Cost.UnpricedLabourMinutes)} hours of people with no pay record aren't in the labour cost.");
        }

        // at most six figures, so it fits headline figures as it is
        var headline = new ProtonDataset(
            $"{p.Title}: profitability",
            [ProtonDatasets.Label("project", "Project"), usd("agreed_revenue", "Agreed revenue"), usd("cost", "Cost"),
                usd("agreed_profit", "Profit (vs agreed)"), ProtonDatasets.Figure("margin", "Margin", ProtonUnit.Percent),
                usd("received", "Received (with TVA)"), site ? usd("site_cash_profit", "Site cash profit") : usd("owed", "Owed (with TVA)")],
            [ProtonDatasets.Row(
                new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, p.Domain),
                ProtonCell.Of(p.Title), ProtonCell.Of(p.AgreedRevenue), ProtonCell.Of(p.Cost.Total), ProtonCell.Of(p.AgreedProfit),
                ProtonCell.Of(ProtonAnalysis.Pct(p.Margin)), ProtonCell.Of(p.Received), ProtonCell.Of(site ? p.SiteCashProfit : p.Owed))],
            "All time",
            Notes: notes);
        var bridge = new ProtonDataset(
            $"{p.Title}: where the agreed profit comes from",
            [ProtonDatasets.Label("step", "Step"), usd("amount", "Amount")],
            p.Bridge.Select(s => ProtonDatasets.Row(null, ProtonCell.Of(ProtonInsights.StepName(s.Kind)), ProtonCell.Of(s.Amount))).ToList(),
            "All time",
            Notes:
            [
                p.BillingType == BillingType.Client
                    ? $"In order, from the base price to the agreed profit of {ProtonInsights.Usd(p.AgreedProfit)}: the steps add up to it. Net of TVA."
                    : $"Internal work has no price: these are its costs, adding up to {ProtonInsights.Usd(p.AgreedProfit)}.",
            ]);
        return ProtonDatasets.WithRows(headline, bridge);
    }
}

public sealed record ProjectScenarioInput
{
    [Description("The project, from find.")]
    public required long ProjectId { get; init; }

    [Description("A variation order in USD, net of TVA: on a site's work price (its supervision and TVA follow), on a Design project's price. Negative takes work out.")]
    public decimal? VoAmount { get; init; }

    [Description("An extra cost in USD; give extra_cost_source with it.")]
    public decimal? ExtraCost { get; init; }

    [Description("Where the extra cost goes: labour or expenses on a Design project, site_expense or ground_labour on a site.")]
    public CostSource? ExtraCostSource { get; init; }

    [Description("Sites only: the supervision rate as a fraction of the work price, 0.10 for 10%.")]
    public decimal? SupervisionRate { get; init; }

    [Description("The TVA rate as a fraction, 0.11 for 11%. TVA is never revenue: it changes what the client pays and owes, not profit.")]
    public decimal? TvaRate { get; init; }

    [Description("Completing with what the client still owes excused (a discount) or written_off (lost): the price drops to what they paid.")]
    public OwedSettlement? SettleOwed { get; init; }

    [Description("Finishing this many weeks later, 1 to 104, at the project's average weekly cost over its last 4 full weeks.")]
    public int? DelayWeeks { get; init; }
}

/// <param name="AgreedWithTva">What the client would be agreed to pay, TVA included.</param>
/// <param name="Owed">What the client would still owe, TVA included.</param>
public sealed record ScenarioFigures(
    decimal AgreedRevenue, decimal AgreedWithTva, decimal Cost, decimal AgreedProfit, int? AgreedMarginPct, decimal Owed, ProfitStanding? Standing)
{
    public static ScenarioFigures Of(ScenarioFiguresResponse f)
        => new(f.AgreedRevenue, f.AgreedWithTva, f.Cost, f.AgreedProfit, ProtonAnalysis.Pct(f.Margin), f.Owed, f.Standing);
}

/// <param name="FloorWouldRefuse">As real changes, the ones other than a settlement would be refused by the profit floor and kept as a blocked change.</param>
/// <param name="WeeklyCost">With a delay: the weekly cost it was priced at, the average of the last four full weeks.</param>
public sealed record ProjectScenario(
    long ProjectId,
    ScenarioFigures Before,
    ScenarioFigures After,
    decimal AgreedProfitChange,
    bool FloorWouldRefuse,
    decimal? WeeklyCost,
    string Note);

/// <summary>
/// One project's figures today and with the changes asked for, as GET /api/finance/insights/projects/{id}/scenario
/// returns them. A simulation: nothing is saved, so it needs no card.
/// </summary>
public sealed class SimulateProjectFinancialScenarioTool(IFinanceInsightService insights) : ProtonTool<ProjectScenarioInput>
{
    public override string Name => "simulate_project_financial_scenario";

    public override string Description =>
        "What a change would do to one project's money, simulated: a variation order, an extra cost by source, a site's " +
        "supervision rate or the TVA rate, settling what the client still owes, or finishing some weeks later. Give at least " +
        "one; rates are fractions (0.10 is 10%). Returns the figures before and after (agreed revenue, with TVA, cost, agreed " +
        "profit, margin, owed, standing), the change in agreed profit and whether the profit floor would refuse it as a real " +
        "change. Nothing is saved: it changes nothing in the app.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<FinanceInsightsController>(nameof(FinanceInsightsController.Scenario));

    protected override async Task<ProtonToolOutput> RunAsync(ProjectScenarioInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var query = new ProjectScenarioQuery
        {
            VoAmount = input.VoAmount,
            ExtraCost = input.ExtraCost,
            ExtraCostSource = input.ExtraCostSource,
            SupervisionRate = input.SupervisionRate,
            TvaRate = input.TvaRate,
            SettleOwed = input.SettleOwed,
            DelayWeeks = input.DelayWeeks,
        };
        await context.ValidateAsync(query, cancellationToken);
        var s = await insights.ScenarioAsync(input.ProjectId, query, cancellationToken);
        var scenario = new ProjectScenario(
            s.ProjectId, ScenarioFigures.Of(s.Before), ScenarioFigures.Of(s.After), s.ProfitChange, s.FloorWouldRefuse, s.WeeklyCost,
            "A simulation: nothing was saved or changed. Agreed revenue, cost and agreed profit are net of TVA; agreed_with_tva " +
            "and owed include it. basis.rules says what was changed.");
        // the project was found by an earlier tool in this conversation: its name, for a link; the scenario names none
        var known = context.KnownRecord(ProtonRefType.Project, s.ProjectId);
        var name = known?.Name ?? $"project #{s.ProjectId}";
        return new ProtonToolOutput(scenario, known is null ? [] : [known])
        {
            Basis = ProtonInsights.Basis($"Scenario on {name}", s.ProjectId, s.Basis,
                "floor_would_refuse: as real changes they would take the agreed profit to 0 or below and lower it, so the profit " +
                "floor would refuse them and keep them as a blocked change; a settlement isn't held to the floor",
                ProtonInsights.StandingRule),
            Datasets = Datasets(name, s),
        };
    }

    /// <summary>The figures with the changes, compared with today's: headline figures show each one's change.</summary>
    private static IReadOnlyList<ProtonDataset> Datasets(string name, ProjectScenarioResponse s)
    {
        var usd = (string key, string label) => ProtonDatasets.Figure(key, label, ProtonUnit.Usd);
        ProtonDataRow Row(string label, ScenarioFiguresResponse f) => ProtonDatasets.Row(
            null, ProtonCell.Of(label), ProtonCell.Of(f.AgreedRevenue), ProtonCell.Of(f.AgreedWithTva), ProtonCell.Of(f.Cost),
            ProtonCell.Of(f.AgreedProfit), ProtonCell.Of(ProtonAnalysis.Pct(f.Margin)), ProtonCell.Of(f.Owed));
        var notes = new List<string> { "A simulation: nothing was saved. Agreed revenue, cost and profit are net of TVA; with TVA and owed include it." };
        if (s.FloorWouldRefuse)
        {
            notes.Add("As a real change, the profit floor would refuse it.");
        }

        return
        [
            new ProtonDataset(
                $"{name}: today and with the changes",
                [ProtonDatasets.Label("scenario", "Scenario"), usd("agreed_revenue", "Agreed revenue"), usd("agreed_with_tva", "Agreed with TVA"),
                    usd("cost", "Cost"), usd("agreed_profit", "Profit (vs agreed)"), ProtonDatasets.Figure("margin", "Margin", ProtonUnit.Percent),
                    usd("owed", "Owed (with TVA)")],
                [Row("With the changes", s.After), Row("Today", s.Before)],
                Compared: true,
                Notes: notes),
        ];
    }
}
