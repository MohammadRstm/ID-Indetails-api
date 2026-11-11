using System.ComponentModel;
using System.Globalization;
using ID_Indetails.api.Business.Finance.Intelligence;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Business.Proton;
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
/// <param name="PlannedMinutes">The hours planned for it, in minutes; null without a plan.</param>
/// <param name="ProgressPct">Minutes logged as a whole percentage of the planned ones (above 100 once past the plan); null without a plan.</param>
public sealed record InsightDepartmentRow(
    long? Id,
    string Name,
    decimal? SharePercent,
    decimal Budget,
    decimal Cost,
    decimal Left,
    int MinutesLogged,
    decimal? LabourSharePercent,
    IReadOnlyList<CurrencyAmountResponse>? UnconvertedLabour,
    int? PlannedMinutes,
    int? ProgressPct);

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

    /// <summary>A domain's insight: the days it covers (today for a register), its scope, and every rule behind a judgement in it.</summary>
    public static ProtonBasis Basis(string label, DayRange period, bool partial, string scope, InsightBasisResponse basis, IEnumerable<string> rules)
        => new(label, period, null, partial, null, scope, new[] { basis.Method }.Concat(basis.Assumptions).Concat(rules).Distinct().ToList(), basis.LeftOut);

    public static string DomainName(string domain) => domain == DomainCodes.Design ? "Design" : "Execution";

    /// <summary>Amounts in their own currency, as "4,500,000 LBP".</summary>
    public static IReadOnlyList<string> Amounts(IEnumerable<CurrencyAmountResponse> amounts)
        => amounts.Select(a => $"{a.Amount.ToString("#,0.##", CultureInfo.InvariantCulture)} {a.Currency}").ToList();

    public static string RiskName(ProjectRiskKind kind) => kind switch
    {
        ProjectRiskKind.ProfitAtZero => "Profit at zero or below",
        ProjectRiskKind.ShortRunway => "Site money short",
        ProjectRiskKind.ProjectedLoss => "Projected loss",
        ProjectRiskKind.ProfitClose => "Profit close to zero",
        ProjectRiskKind.BlockedChange => "Refused change waiting",
        ProjectRiskKind.DepartmentOverBudget => "Department over budget",
        ProjectRiskKind.UnconvertedCosts => "Costs not converted",
        _ => "Settled at zero or below",
    };

    public static string AnomalyName(ExpenseAnomalyKind kind) => kind switch
    {
        ExpenseAnomalyKind.CategoryHigh => "Category month above its average",
        ExpenseAnomalyKind.UnitCostJump => "Unit cost jump",
        _ => "Same amount entered twice",
    };

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
        "each cost, pocket money); and on a Design project each department's share, budget, cost, minutes, share of the " +
        "labour and, where it has planned hours, those and its progress by hours (logged as a percentage of planned), never its people. Use it for \"how profitable is it\", \"where does its profit come from\", \"how close is it to the floor\".";

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
                d.Id, d.Name, d.Share * 100, d.Budget, d.Cost, d.Left, d.Minutes, d.LabourShare * 100,
                d.Unconverted.Count > 0 ? d.Unconverted : null, d.PlannedMinutes, ProtonAnalysis.Pct(d.Progress))).ToList(),
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

public sealed record ProjectRisksInput
{
    [Description("design or execution. May be left out only when the user sees one of them.")]
    public ProtonDomain? Domain { get; init; }

    [Description("Projects to return, most pressing first, 1 to 20 (default 8).")]
    public int? Limit { get; init; }
}

/// <param name="Amount">The USD figure the risk is about, net of TVA, where there is one: a loss, a projected profit, an overspend, the profit a settlement took.</param>
/// <param name="Fact">What was measured, in words: quote it.</param>
/// <param name="Rule">The rule that fired, in words: say it with the risk.</param>
public sealed record InsightRisk(ProjectRiskKind Kind, decimal? Amount, string Fact, string Rule);

/// <summary>A client project and every risk that fired for it, most pressing first. It names no person.</summary>
public sealed record InsightRiskProject(long ProjectId, string Title, ProjectStatus Status, IReadOnlyList<InsightRisk> Risks);

/// <summary>How many projects have a risk of this kind.</summary>
public sealed record InsightRiskCount(ProjectRiskKind Kind, int Projects);

/// <summary>A domain's risk register, as GET /api/finance/insights/{domain}/risks returns it.</summary>
/// <param name="Checked">How many client projects were checked.</param>
/// <param name="WithRisks">How many of them had at least one rule fire.</param>
/// <param name="Projects">The most pressing ones, in the API's order.</param>
/// <param name="More">How many more projects with a risk there are than are listed.</param>
/// <param name="Counts">How many projects have each kind of risk, all of them counted, not only those listed.</param>
public sealed record ProjectRiskRegister(
    string Domain,
    DateOnly Today,
    int Checked,
    int WithRisks,
    IReadOnlyList<InsightRiskProject> Projects,
    int More,
    IReadOnlyList<InsightRiskCount> Counts,
    string Note);

/// <summary>
/// A domain's client projects with a financial risk, most pressing first, each risk with its fact and the rule that
/// fired. It loads every project's money, so it is heavy. Internal projects are cost centres and never listed.
/// </summary>
public sealed class DetectProjectFinancialRisksTool(IDomainInsightService insights) : ProtonTool<ProjectRisksInput>
{
    public const int DefaultLimit = 8;
    public const int MaxLimit = 20;

    public override string Name => "detect_project_financial_risks";

    public override string Description =>
        "A domain's client projects with a financial risk, most pressing first: an agreed profit at zero or below, a site's " +
        "cash profit below zero or running out soon, a projected loss, a margin close to the profit floor, a refused change " +
        "waiting, a Design department over its budget, costs with no exchange rate, or a settlement that ended at zero or " +
        "below. Each risk has its amount (USD net of TVA), the measured fact and the rule that fired; also how many projects " +
        "were checked and the counts by kind. Internal projects are never listed. Use it for \"which projects are at risk\", " +
        "\"is any project losing money\", \"what should I worry about financially\".";

    public override bool Heavy => true;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<FinanceInsightsController>(nameof(FinanceInsightsController.Risks));

    protected override async Task<ProtonToolOutput> RunAsync(ProjectRisksInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var domain = context.DomainOf(input.Domain);
        var r = await insights.RisksAsync(domain, cancellationToken);
        var name = ProtonInsights.DomainName(r.Domain);
        var shown = r.Projects.Take(ProtonText.Limit(input.Limit, DefaultLimit, MaxLimit)).ToList();
        var register = new ProjectRiskRegister(
            r.Domain,
            r.Today,
            r.Checked,
            r.Projects.Count,
            shown.Select(p => new InsightRiskProject(
                p.ProjectId, p.Title, p.Status, p.Risks.Select(k => new InsightRisk(k.Kind, k.Amount, k.Fact, k.Rule)).ToList())).ToList(),
            r.Projects.Count - shown.Count,
            r.Counts.Select(c => new InsightRiskCount(c.Kind, c.Projects)).ToList(),
            "Most pressing first: by each project's most pressing risk, in the order of the kinds (profit_at_zero, short_runway, " +
            "projected_loss, profit_close, blocked_change, department_over_budget, unconverted_costs, settlement_at_zero), then " +
            "its size. Amounts are USD net of TVA, all time. Each risk is a rule that fired: quote its fact and say its rule, " +
            "never a judgement of your own. A projection is an estimate. A running client project not listed had no rule " +
            "fire; a finished one is checked only for a recent settlement, and internal projects never (basis says so).");
        return new ProtonToolOutput(register, shown.Select(p => new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, r.Domain)).ToList())
        {
            Basis = ProtonInsights.Basis(
                $"{name}'s financial risks", new DayRange(r.Today, r.Today), false, $"{name}: client projects, as of today", r.Basis,
                r.Projects.SelectMany(p => p.Risks.Select(k => k.Rule))),
            Datasets = Datasets(name, r, shown),
        };
    }

    /// <summary>
    /// The risk register as a table (each project's most pressing risk and its fact, never a figure to chart: the amounts
    /// mean different things), and how many projects have each kind of risk.
    /// </summary>
    private static IReadOnlyList<ProtonDataset> Datasets(string name, ProjectRisksResponse r, IReadOnlyList<ProjectRiskGroupResponse> shown)
    {
        var asOf = ProtonDatasets.Period(r.Today, r.Today);
        var register = new ProtonDataset(
            $"{name}: risk register",
            [ProtonDatasets.Label("project", "Project"), ProtonDatasets.Label("status", "Status"), ProtonDatasets.Label("risk", "Most pressing risk"),
                ProtonDatasets.Label("fact", "What was measured"), ProtonDatasets.Label("other_risks", "Other risks")],
            shown.Select(p => ProtonDatasets.Row(
                new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, r.Domain),
                ProtonCell.Of(p.Title), ProtonDatasets.Status(p.Status), ProtonCell.Of(ProtonInsights.RiskName(p.Risks[0].Kind)),
                ProtonCell.Of(p.Risks[0].Fact),
                ProtonCell.Of(p.Risks.Count > 1 ? string.Join(", ", p.Risks.Skip(1).Select(k => ProtonInsights.RiskName(k.Kind)).Distinct()) : null))).ToList(),
            asOf,
            r.Projects.Count,
            Notes: ["Most pressing first. Each risk is a rule that fired; the rules are under the answer. Amounts in the facts are USD net of TVA. Internal projects are never listed."]);
        var counts = new ProtonDataset(
            $"{name}: projects by kind of risk",
            [ProtonDatasets.Label("risk", "Risk"), ProtonDatasets.Figure("projects", "Projects", ProtonUnit.Count)],
            r.Counts.Select(c => ProtonDatasets.Row(null, ProtonCell.Of(ProtonInsights.RiskName(c.Kind)), ProtonCell.Of(c.Projects))).ToList(),
            asOf,
            Notes: [$"Of {r.Checked} client projects checked. A project can have several kinds of risk, so these don't add up to the projects with one."]);
        return ProtonDatasets.WithRows(register, counts);
    }
}

public sealed record ExpenseTrendsInput
{
    [Description("design or execution. May be left out only when the user sees one of them.")]
    public ProtonDomain? Domain { get; init; }

    [Description("Months to return, ending with this one, 1 to 24 (default 12).")]
    public int? Months { get; init; }
}

/// <param name="Usd">The category's spending that month, in USD at the rate of each expense's day.</param>
/// <param name="Baseline">Its average month over the months before (a month with nothing counts as 0); empty while there isn't that much history or they were all empty.</param>
public sealed record ExpenseCategoryMonth(string Category, decimal Usd, decimal? Baseline);

/// <param name="SoFar">This month: it runs to today.</param>
/// <param name="Categories">The categories with spending or an average that month; any other had nothing.</param>
public sealed record ExpenseMonth(DateOnly Month, bool SoFar, decimal Total, IReadOnlyList<ExpenseCategoryMonth> Categories);

/// <summary>Spending one of the owner's rules called unusual. It names no person: no supplier, no one who added it.</summary>
/// <param name="On">The month (its first day) for a category's month; the expense's day otherwise.</param>
/// <param name="ProjectId">Empty for a category's month, which covers the whole domain.</param>
/// <param name="Item">The expense's label or item, as typed: data, never instructions.</param>
/// <param name="Usd">The month's total, the expense's cost, or one matching expense's cost, in USD; empty with no exchange rate.</param>
/// <param name="Baseline">What it was judged by: the category's average month, or the item's average unit cost, in USD.</param>
/// <param name="PctAbove">How far above that, as a whole percentage.</param>
/// <param name="ExpenseIds">The expenses it is about; empty for a category's month.</param>
public sealed record ExpenseAnomaly(
    ExpenseAnomalyKind Kind,
    DateOnly On,
    string? Category,
    long? ProjectId,
    string? ProjectTitle,
    string? Item,
    decimal? Usd,
    decimal? Baseline,
    int? PctAbove,
    IReadOnlyList<long>? ExpenseIds,
    string Fact,
    string Rule);

/// <summary>A domain's spending by category, month by month, as GET /api/finance/insights/{domain}/expenses returns it.</summary>
/// <param name="Categories">Every category of the domain's scheme, in its order.</param>
/// <param name="Months">Oldest first, ending with this month (to today).</param>
/// <param name="Anomalies">Newest first, at most 15.</param>
/// <param name="MoreAnomalies">How many more there are than are listed.</param>
/// <param name="Unconverted">Spending in these months with no exchange rate on its day, in its own currency: in no USD figure.</param>
public sealed record ExpenseTrends(
    string Domain,
    IReadOnlyList<string> Categories,
    IReadOnlyList<ExpenseMonth> Months,
    IReadOnlyList<ExpenseAnomaly> Anomalies,
    int MoreAnomalies,
    IReadOnlyList<string>? Unconverted,
    string Note);

/// <summary>
/// A domain's spending by category month by month, with each category's average of the months before, and what the
/// owner's rules call unusual in it. It reads every project's expenses, so it is heavy. Expenses carry no person.
/// </summary>
public sealed class AnalyzeExpenseTrendsTool(IDomainInsightService insights) : ProtonTool<ExpenseTrendsInput>
{
    public const int MaxAnomalies = 15;

    public override string Name => "analyze_expense_trends";

    public override string Description =>
        "A domain's spending month by month (12 months by default, up to 24, ending with this one), in USD at the rate of " +
        "each expense's day: Design's project expenses or Execution's site expenses, by category, internal projects included, " +
        "with each category's average over the months before. Also what the owner's rules call unusual: a category's month " +
        "far above its average, an item bought at a unit cost far above its last purchases (site expenses), or the same item, " +
        "amount and currency entered twice on one project, each with its fact and the rule that fired; and spending with no " +
        "exchange rate, listed apart. Use it for \"how is our spending going\", \"spending by category\", \"anything unusual in the expenses\".";

    public override bool Heavy => true;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<FinanceInsightsController>(nameof(FinanceInsightsController.Expenses));

    protected override async Task<ProtonToolOutput> RunAsync(ExpenseTrendsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var domain = context.DomainOf(input.Domain);
        var query = new ExpenseTrendsQuery { Months = input.Months };
        await context.ValidateAsync(query, cancellationToken);
        var e = await insights.ExpensesAsync(domain, query, cancellationToken);
        var name = ProtonInsights.DomainName(e.Domain);
        var thisMonth = new DateOnly(context.Today.Year, context.Today.Month, 1);
        var anomalies = e.Anomalies.Take(MaxAnomalies).ToList();
        var trends = new ExpenseTrends(
            e.Domain,
            e.Categories,
            e.Months.Select(m => new ExpenseMonth(
                m.Month, m.Month == thisMonth, m.Total,
                m.Categories.Where(c => c.Usd != 0 || c.Baseline is not null).Select(c => new ExpenseCategoryMonth(c.Category, c.Usd, c.Baseline)).ToList())).ToList(),
            anomalies.Select(a => new ExpenseAnomaly(
                a.Kind, a.On, a.Category, a.ProjectId, a.ProjectTitle, a.Item, a.Usd, a.Baseline, a.Pct,
                a.ExpenseIds.Count > 0 ? a.ExpenseIds : null, a.Fact, a.Rule)).ToList(),
            e.Anomalies.Count - anomalies.Count,
            e.Unconverted.Count > 0 ? ProtonInsights.Amounts(e.Unconverted) : null,
            "In USD at the rate of each expense's day; this month runs to today, so its figures are so far. A baseline is the " +
            "category's average month over the months before; empty while there isn't that much history. Spending is unusual " +
            "only where an anomaly lists it: quote its fact and say its rule, never a judgement of your own. Unconverted " +
            "spending is in no USD figure. Expenses name no supplier and no one who added them.");
        var refs = anomalies
            .Where(a => a.ProjectId is not null && a.ProjectTitle is not null)
            .Select(a => new ProtonRef(ProtonRefType.Project, a.ProjectId!.Value, a.ProjectTitle!, e.Domain))
            .DistinctBy(r => r.Id)
            .ToList();
        var period = new DayRange(e.Months.Count > 0 ? e.Months[0].Month : thisMonth, context.Today);
        return new ProtonToolOutput(trends, refs, period)
        {
            Basis = ProtonInsights.Basis(
                $"{name}'s spending by category", period, true,
                $"{name}: {(e.Domain == DomainCodes.Design ? "project expenses" : "site expenses")}, internal projects included", e.Basis,
                anomalies.Select(a => a.Rule)),
            Datasets = Datasets(name, e, anomalies, period, thisMonth),
        };
    }

    /// <summary>
    /// Spending by month with a column per category that had any (a chart over time picks up to three), and the
    /// anomalies as a table.
    /// </summary>
    private static IReadOnlyList<ProtonDataset> Datasets(
        string name, ExpenseTrendsResponse e, IReadOnlyList<ExpenseAnomalyResponse> anomalies, DayRange period, DateOnly thisMonth)
    {
        var usd = (string key, string label) => ProtonDatasets.Figure(key, label, ProtonUnit.Usd);
        var words = ProtonDatasets.Period(period);
        var notes = new List<string> { "In USD at the rate of each expense's day. Internal projects included." };
        if (e.Unconverted.Count > 0)
        {
            notes.Add($"Left out for want of an exchange rate on their day: {string.Join(", ", ProtonInsights.Amounts(e.Unconverted))}.");
        }

        // the categories with any spending in these months, in the scheme's order
        var spent = e.Categories.Where(c => e.Months.Any(m => m.Categories.Any(x => x.Category == c && x.Usd != 0))).ToList();
        var byMonth = spent.Count == 0 ? null : new ProtonDataset(
            $"{name}: spending by category, by month",
            [ProtonDatasets.Label("month", "Month", ProtonColumnKind.Month), usd("total", "Total"),
                .. spent.Select(c => usd(c, ExpenseTrendRules.CategoryName(c)))],
            e.Months.Select(m => new ProtonDataRow(
                [ProtonCell.Of(m.Month), ProtonCell.Of(m.Total),
                    .. spent.Select(c => ProtonCell.Of(m.Categories.FirstOrDefault(x => x.Category == c)?.Usd ?? 0m))],
                Partial: m.Month == thisMonth)).ToList(),
            words,
            Notes: notes);
        var unusual = new ProtonDataset(
            $"{name}: spending the rules call unusual",
            [ProtonDatasets.Label("anomaly", "Rule that fired"), ProtonDatasets.Label("on", "On", ProtonColumnKind.Date),
                ProtonDatasets.Label("category", "Category"), ProtonDatasets.Label("project", "Project"), usd("usd", "Amount"),
                ProtonDatasets.Figure("pct_above", "Above its average", ProtonUnit.Percent), ProtonDatasets.Label("fact", "What was measured")],
            anomalies.Select(a => ProtonDatasets.Row(
                a.ProjectId is { } id && a.ProjectTitle is { } title ? new ProtonRef(ProtonRefType.Project, id, title, e.Domain) : null,
                ProtonCell.Of(ProtonInsights.AnomalyName(a.Kind)), ProtonCell.Of(a.On),
                ProtonCell.Of(a.Category is { } category ? ExpenseTrendRules.CategoryName(category) : null), ProtonCell.Of(a.ProjectTitle),
                ProtonCell.Of(a.Usd), ProtonCell.Of(a.Pct), ProtonCell.Of(a.Fact))).ToList(),
            words,
            e.Anomalies.Count,
            Notes: ["Newest first. Each row is a rule that fired; the rules are under the answer. A category's month covers the whole domain and its amount is the month's total; otherwise the amount is one expense's cost."]);
        return ProtonDatasets.WithRows(byMonth, unusual);
    }
}

public sealed record LabourInsightInput
{
    [Description("Months to return, ending with this one, 1 to 24 (default 12).")]
    public int? Months { get; init; }
}

/// <summary>Labour over a span: never one person's.</summary>
/// <param name="Cost">USD at the rate of each day: hours at each person's hourly cost that day, the overtime Salaries pays, and paid breaks.</param>
/// <param name="OutsideScopeCost">The part of <paramref name="Cost"/> logged outside the agreed scope (its worked time; paid breaks stay with the day).</param>
/// <param name="OutsideScopePct">Outside-scope cost as a whole percentage of the cost; empty with no cost.</param>
public sealed record LabourFigures(decimal Cost, decimal Hours, decimal OutsideScopeCost, decimal OutsideScopeHours, int? OutsideScopePct)
{
    public static LabourFigures Of(LabourTotalsResponse t)
        => new(t.Cost, ProtonText.Hours(t.Minutes), t.OutsideScopeCost, ProtonText.Hours(t.OutsideScopeMinutes), ProtonAnalysis.Pct(t.OutsideScopeShare));
}

/// <param name="SoFar">This month: it runs to today.</param>
public sealed record LabourMonthRow(DateOnly Month, bool SoFar, LabourFigures Labour);

/// <param name="SharePct">Its part of the span's labour cost, a whole percentage; empty with no cost.</param>
public sealed record LabourProjectRow(long ProjectId, string Title, BillingType BillingType, LabourFigures Labour, int? SharePct);

/// <summary>A department's labour. It never lists the people in it.</summary>
/// <param name="Id">Null for the hours of people in no department.</param>
/// <param name="SharePct">Its part of the span's labour cost, a whole percentage; empty with no cost.</param>
public sealed record LabourDepartmentRow(long? Id, string Name, LabourFigures Labour, int? SharePct);

/// <summary>Design's labour over the last months, as GET /api/finance/insights/design/labour returns it. No figure is about one person.</summary>
/// <param name="Months">Oldest first, ending with this month (to today).</param>
/// <param name="Projects">The costliest first, internal projects included, at most 15.</param>
/// <param name="MoreProjects">How many more projects had labour than are listed.</param>
/// <param name="Departments">The costliest first.</param>
/// <param name="Unconverted">Labour with no exchange rate on its day, in its own currency: in no cost.</param>
public sealed record LabourInsight(
    string Domain,
    LabourFigures Total,
    IReadOnlyList<LabourMonthRow> Months,
    IReadOnlyList<LabourProjectRow> Projects,
    int MoreProjects,
    IReadOnlyList<LabourDepartmentRow> Departments,
    IReadOnlyList<string>? Unconverted,
    string Note);

/// <summary>
/// Design's labour cost and hours month by month, by project and by department, with the part logged outside the agreed
/// scope. It prices every project's hours, so it is heavy. Design only: Execution logs no hours (its labour is ground
/// labour, in its spending). Never a person: every figure is a sum over a month, a project or a department.
/// </summary>
public sealed class AnalyzeLaborCostsTool(IDomainInsightService insights) : ProtonTool<LabourInsightInput>
{
    public const int MaxProjects = 15;

    public override string Name => "analyze_labor_costs";

    public override string Description =>
        "Design's labour cost and hours month by month (12 months by default, up to 24, ending with this one), in USD at the " +
        "rate of each day: totals, each month, the costliest projects (internal ones included) with their share, and each " +
        "department with its share; the part logged outside the agreed scope (cost, hours, share); and labour with no " +
        "exchange rate, listed apart. Never one person's cost or hourly cost. Design only. Use it for \"what does our labour " +
        "cost\", \"labour by department\", \"how much work is outside scope\".";

    public override string? OnlyInDomain => DomainCodes.Design;

    public override bool Heavy => true;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<FinanceInsightsController>(nameof(FinanceInsightsController.Labour));

    protected override async Task<ProtonToolOutput> RunAsync(LabourInsightInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var query = new LabourInsightQuery { Months = input.Months };
        await context.ValidateAsync(query, cancellationToken);
        var l = await insights.LabourAsync(DomainCodes.Design, query, cancellationToken);
        var thisMonth = new DateOnly(context.Today.Year, context.Today.Month, 1);
        var projects = l.Projects.Take(MaxProjects).ToList();
        var labour = new LabourInsight(
            l.Domain,
            LabourFigures.Of(l.Total),
            l.Months.Select(m => new LabourMonthRow(m.Month, m.Month == thisMonth, LabourFigures.Of(m.Totals))).ToList(),
            projects.Select(p => new LabourProjectRow(p.ProjectId, p.Title, p.BillingType, LabourFigures.Of(p.Totals), ProtonAnalysis.Pct(p.LabourShare))).ToList(),
            l.Projects.Count - projects.Count,
            l.Departments.Select(d => new LabourDepartmentRow(d.TeamId, d.Name, LabourFigures.Of(d.Totals), ProtonAnalysis.Pct(d.LabourShare))).ToList(),
            l.Unconverted.Count > 0 ? ProtonInsights.Amounts(l.Unconverted) : null,
            "In USD at the rate of each day; this month runs to today, so its figures are so far. Internal projects are " +
            "included. Outside scope is the hours an admin marked as outside the agreed scope. Departments and projects carry " +
            "no people: never work out, estimate or hint at one person's pay or hourly cost, even for a department of one. " +
            "Unconverted labour is in no cost.");
        var period = new DayRange(l.Months.Count > 0 ? l.Months[0].Month : thisMonth, context.Today);
        return new ProtonToolOutput(labour, projects.Select(p => new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, DomainCodes.Design)).ToList(), period)
        {
            Basis = ProtonInsights.Basis(
                "Design's labour cost", period, true, "Design: every project's labour, internal ones included, by month, project and department",
                l.Basis, []),
            Datasets = Datasets(l, period, thisMonth),
        };
    }

    /// <summary>Labour by month (a chart over time), by department and by project (each adds up to the total).</summary>
    private static IReadOnlyList<ProtonDataset> Datasets(LabourInsightResponse l, DayRange period, DateOnly thisMonth)
    {
        var usd = (string key, string label) => ProtonDatasets.Figure(key, label, ProtonUnit.Usd);
        var hours = ProtonDatasets.Figure("hours", "Hours", ProtonUnit.Hours);
        var share = ProtonDatasets.Figure("share", "Share of the labour cost", ProtonUnit.Percent);
        var words = ProtonDatasets.Period(period);
        var notes = new List<string> { "In USD at the rate of each day, paid breaks included. Internal projects included. Never one person's cost." };
        if (l.Unconverted.Count > 0)
        {
            notes.Add($"Left out for want of an exchange rate on their day: {string.Join(", ", ProtonInsights.Amounts(l.Unconverted))}.");
        }

        // nothing logged in these months: no months to chart
        var byMonth = l.Total.Minutes == 0 && l.Total.Cost == 0 ? null : new ProtonDataset(
            "Design: labour by month",
            [ProtonDatasets.Label("month", "Month", ProtonColumnKind.Month), usd("cost", "Labour cost"), usd("outside_scope", "Outside scope"), hours],
            l.Months.Select(m => new ProtonDataRow(
                [ProtonCell.Of(m.Month), ProtonCell.Of(m.Totals.Cost), ProtonCell.Of(m.Totals.OutsideScopeCost), ProtonCell.Of(ProtonText.Hours(m.Totals.Minutes))],
                Partial: m.Month == thisMonth)).ToList(),
            words,
            Notes: notes);
        var departments = new ProtonDataset(
            "Design: labour by department",
            [ProtonDatasets.Label("department", "Department"), usd("cost", "Labour cost"), hours, share],
            l.Departments.Take(ProtonDatasets.MaxRows).Select(d => ProtonDatasets.Row(
                null, ProtonCell.Of(d.Name), ProtonCell.Of(d.Totals.Cost), ProtonCell.Of(ProtonText.Hours(d.Totals.Minutes)),
                ProtonCell.Of(ProtonAnalysis.Pct(d.LabourShare)))).ToList(),
            words,
            l.Departments.Count,
            PartsOfWhole: true,
            Notes: ["A department's labour, never its people. The department is the one the hours were logged in."]);
        var projects = new ProtonDataset(
            "Design: labour by project",
            [ProtonDatasets.Label("project", "Project"), usd("cost", "Labour cost"), hours, usd("outside_scope", "Outside scope"), share],
            l.Projects.Take(ProtonDatasets.MaxRows).Select(p => ProtonDatasets.Row(
                new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, DomainCodes.Design),
                ProtonCell.Of(p.Title), ProtonCell.Of(p.Totals.Cost), ProtonCell.Of(ProtonText.Hours(p.Totals.Minutes)),
                ProtonCell.Of(p.Totals.OutsideScopeCost), ProtonCell.Of(ProtonAnalysis.Pct(p.LabourShare)))).ToList(),
            words,
            l.Projects.Count,
            PartsOfWhole: true,
            Notes: ["The costliest first, internal projects included."]);
        return ProtonDatasets.WithRows(byMonth, departments, projects);
    }
}
