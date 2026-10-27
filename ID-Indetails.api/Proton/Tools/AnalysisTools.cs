using System.ComponentModel;
using System.Globalization;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Reports;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Analysis;
using ID_Indetails.api.Proton.Ui;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>A figure in a period and, when compared, the one before: the difference and percentage are calculated here.</summary>
/// <param name="Pct">The difference as a whole percentage of <paramref name="Previous"/>; null when there was nothing before.</param>
public sealed record ProtonFigure(decimal Current, decimal? Previous, decimal? Diff, int? Pct)
{
    public static ProtonFigure Of(decimal current, decimal? previous)
    {
        if (previous is not { } before)
        {
            return new ProtonFigure(current, null, null, null);
        }

        var change = ProtonTrends.Change(current, before);
        return new ProtonFigure(current, before, change.Diff, change.Pct);
    }
}

/// <summary>The pieces every analytical tool's result is built from.</summary>
public static class ProtonAnalysis
{
    public static ProtonBasis Basis(string label, ProtonPeriodPair pair, string? scope, IEnumerable<string> rules, IEnumerable<string> omitted)
        => new(label, pair.Current, pair.Previous, pair.Partial, pair.Note, scope, rules.Distinct().ToList(), omitted.Distinct().ToList());

    /// <summary>
    /// Rows for each group in either period, biggest now first: the figure, its share of the total, its share of the
    /// total change, and whether the change is notable under <paramref name="floor"/>.
    /// </summary>
    public static IReadOnlyList<ProtonGroupRow> Rows(
        IEnumerable<(long? Id, string Name, decimal Value)> current,
        IEnumerable<(long? Id, string Name, decimal Value)>? previous,
        decimal floor,
        ProtonThresholds t)
    {
        var now = current.ToDictionary(r => r.Id ?? 0);
        var before = previous?.ToDictionary(r => r.Id ?? 0);
        var keys = now.Keys.Union(before?.Keys ?? Enumerable.Empty<long>()).ToList();
        var total = now.Values.Sum(r => r.Value);
        var shares = before is null ? null : ProtonTrends.Contributors(keys.Select(k =>
            (k, now.TryGetValue(k, out var c) ? c.Value : 0m, before.TryGetValue(k, out var p) ? p.Value : 0m)))
            .ToDictionary(c => c.Key, c => c.SharePct);

        return keys
            .Select(k =>
            {
                var row = now.TryGetValue(k, out var c) ? c : before![k];
                decimal? previousValue = before is null ? null : before.TryGetValue(k, out var p) ? p.Value : 0m;
                var figure = ProtonFigure.Of(now.TryGetValue(k, out var n) ? n.Value : 0m, previousValue);
                return new ProtonGroupRow(
                    row.Id,
                    row.Name,
                    figure,
                    ProtonTrends.SharePct(figure.Current, total),
                    shares?.GetValueOrDefault(k),
                    previousValue is not null && ProtonSignals.IsNotable(ProtonTrends.Change(figure.Current, previousValue.Value), floor, t));
            })
            .OrderByDescending(r => r.Value.Current)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<ProtonGroupRow> Increases(IEnumerable<ProtonGroupRow> rows, int count)
        => rows.Where(r => r.Value.Diff > 0).OrderByDescending(r => r.Value.Diff).Take(count).ToList();

    public static IReadOnlyList<ProtonGroupRow> Decreases(IEnumerable<ProtonGroupRow> rows, int count)
        => rows.Where(r => r.Value.Diff < 0).OrderBy(r => r.Value.Diff).Take(count).ToList();

    public static int? Pct(decimal? fraction) => fraction is { } f ? (int)Math.Round(f * 100, MidpointRounding.AwayFromZero) : null;

    /// <summary>Costs left out of USD figures for want of an exchange rate, as "4500000.00 LBP (expenses)".</summary>
    public static IReadOnlyList<string> Unconverted(IEnumerable<UnconvertedCostResponse> costs)
        => costs.Select(c => $"{c.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {c.Currency} ({SourceName(c.Source)})").ToList();

    /// <summary>Said in basis.omitted when a figure leaves out costs it couldn't convert, so a total isn't read as complete.</summary>
    public const string UnconvertedLeftOut = "costs in another currency with no exchange rate on their day: left out of these USD figures";

    private static string SourceName(CostSource source) => source == CostSource.GroundLabour ? "ground labour" : source.ToString().ToLowerInvariant();

    /// <summary>
    /// Compared groups as a table: the figure now, and when compared the figure before and the change. Every group is
    /// kept (up to a table's worth), so the figures now add up to the total.
    /// </summary>
    public static ProtonDataset Groups(
        string title, string key, string label, ProtonPeriodPair pair, IReadOnlyList<ProtonGroupRow> rows, ProtonUnit unit, string figure,
        Func<ProtonGroupRow, ProtonRef?> link, IReadOnlyList<string>? notes = null)
    {
        var compared = pair.Previous is not null;
        IReadOnlyList<ProtonColumn> columns = compared
            ? [ProtonDatasets.Label(key, label), ProtonDatasets.Figure("now", figure, unit), ProtonDatasets.Figure("before", $"{figure} before", unit),
                ProtonDatasets.Figure("change", "Change", unit)]
            : [ProtonDatasets.Label(key, label), ProtonDatasets.Figure("now", figure, unit)];
        return new ProtonDataset(
            title,
            columns,
            rows.Take(ProtonDatasets.MaxRows).Select(r => ProtonDatasets.Row(
                link(r),
                compared
                    ? [ProtonCell.Of(r.Name), ProtonCell.Of(r.Value.Current), ProtonCell.Of(r.Value.Previous), ProtonCell.Of(r.Value.Diff)]
                    : [ProtonCell.Of(r.Name), ProtonCell.Of(r.Value.Current)])).ToList(),
            ProtonDatasets.Period(pair.Current),
            rows.Count,
            PartsOfWhole: true,
            Notes: notes);
    }
}

/// <param name="SharePct">Its share of the current total.</param>
/// <param name="ShareOfChangePct">Its share of the total change; null when not compared or the total didn't change.</param>
/// <param name="Notable">The change passes the approved notable rule (in the basis).</param>
public sealed record ProtonGroupRow(long? Id, string Name, ProtonFigure Value, int SharePct, int? ShareOfChangePct, bool Notable);

public enum WorkGrouping
{
    Total,
    Project,
    Person,
    Client,
}

public sealed record CompareWorkInput
{
    [Description("Default this_month. Running periods (this_week, this_month…) are compared like for like.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    [Description("Default previous. none for a single period.")]
    public ProtonComparison? CompareWith { get; init; }

    [Description("With compare_with custom.")]
    public DateOnly? CompareFrom { get; init; }

    public DateOnly? CompareTo { get; init; }

    [Description("Default project.")]
    public WorkGrouping? GroupBy { get; init; }

    public long? ProjectId { get; init; }

    public long? PersonId { get; init; }

    public long? ClientId { get; init; }

    [Description("Rows to return, 1 to 15 (default 10).")]
    public int? Limit { get; init; }
}

/// <param name="Groups">How many groups there are; <paramref name="Rows"/> holds the first ones.</param>
/// <param name="NewInPeriod">Groups with hours now and none before.</param>
/// <param name="StoppedInPeriod">Groups with hours before and none now.</param>
public sealed record WorkComparison(
    ProtonFigure Hours,
    ProtonFigure Entries,
    ProtonFigure People,
    ProtonFigure Projects,
    bool HoursNotable,
    WorkGrouping GroupedBy,
    int Groups,
    IReadOnlyList<ProtonGroupRow> Rows,
    IReadOnlyList<ProtonGroupRow> BiggestIncreases,
    IReadOnlyList<ProtonGroupRow> BiggestDecreases,
    int NewInPeriod,
    int StoppedInPeriod);

/// <summary>Hours of two periods side by side, from the work report's own totals (GET /api/reports/work). Design only.</summary>
public sealed class CompareWorkTool(ProtonAnalysisService analysis) : ProtonTool<CompareWorkInput>
{
    private const int Movers = 5;

    public override string Name => "compare_work";

    public override string Description =>
        "Hours logged in a period compared with another (the previous one by default, like for like), in total and by " +
        "project, person or client, with the differences, percentages and biggest movers already calculated. Filter by " +
        "project, person or client: e.g. a project grouped by person shows who drove its change. Design only.";

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ReportsController>(nameof(ReportsController.Work));

    protected override async Task<ProtonToolOutput> RunAsync(CompareWorkInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var pair = context.Compare(input.Period, input.From, input.To, ProtonPeriod.ThisMonth, input.CompareWith, input.CompareFrom, input.CompareTo);
        var limit = ProtonText.Limit(input.Limit, 10, 15);
        var grouping = input.GroupBy ?? WorkGrouping.Project;
        var t = analysis.Thresholds;

        var now = await TotalsAsync(input, pair.Current, context, cancellationToken);
        var before = pair.Previous is { } previous ? await TotalsAsync(input, previous, context, cancellationToken) : null;

        var rows = grouping switch
        {
            WorkGrouping.Person => ProtonAnalysis.Rows(Persons(now), before is null ? null : Persons(before), t.NotableHours, t),
            WorkGrouping.Client => ProtonAnalysis.Rows(Clients(now), before is null ? null : Clients(before), t.NotableHours, t),
            WorkGrouping.Project => ProtonAnalysis.Rows(Projects(now), before is null ? null : Projects(before), t.NotableHours, t),
            _ => [],
        };
        var hours = ProtonFigure.Of(ProtonText.Hours(now.TotalMinutes), before is null ? null : ProtonText.Hours(before.TotalMinutes));
        var comparison = new WorkComparison(
            hours,
            ProtonFigure.Of(now.EntryCount, before?.EntryCount),
            ProtonFigure.Of(now.ByPerson.Count, before?.ByPerson.Count),
            ProtonFigure.Of(now.ByProject.Count, before?.ByProject.Count),
            before is not null && ProtonSignals.IsNotable(ProtonTrends.Change(hours.Current, hours.Previous!.Value), t.NotableHours, t),
            grouping,
            rows.Count,
            rows.Take(limit).ToList(),
            ProtonAnalysis.Increases(rows, Movers),
            ProtonAnalysis.Decreases(rows, Movers),
            before is null ? 0 : rows.Count(r => r.Value.Previous == 0 && r.Value.Current > 0),
            before is null ? 0 : rows.Count(r => r.Value.Current == 0 && r.Value.Previous > 0));

        var omitted = new List<string>();
        if (context.Access.CanSee(DomainCodes.Execution))
        {
            omitted.Add(ProtonAnalysisService.NoExecutionHours);
        }

        var refType = grouping switch
        {
            WorkGrouping.Person => ProtonRefType.Person,
            WorkGrouping.Client => ProtonRefType.Client,
            _ => ProtonRefType.Project,
        };
        var refs = grouping == WorkGrouping.Total ? [] : comparison.Rows.Concat(comparison.BiggestIncreases).Concat(comparison.BiggestDecreases)
            .Where(r => r.Id is not null)
            .DistinctBy(r => r.Id)
            .Select(r => new ProtonRef(refType, r.Id!.Value, r.Name, refType == ProtonRefType.Project ? DomainCodes.Design : null))
            .ToList();
        return new ProtonToolOutput(comparison, refs, pair.Current)
        {
            Basis = ProtonAnalysis.Basis(
                "Hours compared", pair, Scope(input), [ProtonSignals.NotableHoursRule(t), ProtonAnalysisService.HoursAreRecorded], omitted),
            Datasets = Datasets(pair, now, before, grouping, rows, refType),
        };
    }

    /// <summary>The comparison as tables: the headline figures of both periods, and the hours of each group.</summary>
    private static IReadOnlyList<ProtonDataset> Datasets(
        ProtonPeriodPair pair,
        DTO.Response.Reports.WorkTotalsResponse now,
        DTO.Response.Reports.WorkTotalsResponse? before,
        WorkGrouping grouping,
        IReadOnlyList<ProtonGroupRow> rows,
        ProtonRefType refType)
    {
        var totals = ProtonDatasets.Compared(
            "Hours compared",
            pair,
            [ProtonDatasets.Figure("hours", "Hours", ProtonUnit.Hours), ProtonDatasets.Figure("entries", "Entries", ProtonUnit.Count),
                ProtonDatasets.Figure("people", "People", ProtonUnit.Count), ProtonDatasets.Figure("projects", "Projects", ProtonUnit.Count)],
            Figures(now),
            before is null ? null : Figures(before));
        var groups = grouping == WorkGrouping.Total ? null : ProtonAnalysis.Groups(
            $"Hours by {grouping.ToString().ToLowerInvariant()}", grouping.ToString().ToLowerInvariant(), grouping.ToString(), pair, rows, ProtonUnit.Hours, "Hours",
            r => r.Id is { } id ? new ProtonRef(refType, id, r.Name, refType == ProtonRefType.Project ? DomainCodes.Design : null) : null);
        return ProtonDatasets.WithRows(totals, groups);
    }

    private static IReadOnlyList<decimal?> Figures(DTO.Response.Reports.WorkTotalsResponse w)
        => [ProtonText.Hours(w.TotalMinutes), w.EntryCount, w.ByPerson.Count, w.ByProject.Count];

    private async Task<DTO.Response.Reports.WorkTotalsResponse> TotalsAsync(
        CompareWorkInput input, DayRange range, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var query = new WorkReportQuery { From = range.From, To = range.To, ProjectId = input.ProjectId, UserId = input.PersonId, ClientId = input.ClientId };
        await context.ValidateAsync(query, cancellationToken);
        return await analysis.WorkAsync(query, cancellationToken);
    }

    private static string Scope(CompareWorkInput input)
    {
        var filters = new List<string>();
        if (input.ProjectId is { } project)
        {
            filters.Add($"project #{project}");
        }

        if (input.PersonId is { } person)
        {
            filters.Add($"person #{person}");
        }

        if (input.ClientId is { } client)
        {
            filters.Add($"client #{client}");
        }

        return filters.Count == 0 ? "all Design work" : "Design work of " + string.Join(", ", filters);
    }

    private static IEnumerable<(long?, string, decimal)> Projects(DTO.Response.Reports.WorkTotalsResponse w)
        => w.ByProject.Select(p => ((long?)p.ProjectId, p.Title, ProtonText.Hours(p.Minutes)));

    private static IEnumerable<(long?, string, decimal)> Persons(DTO.Response.Reports.WorkTotalsResponse w)
        => w.ByPerson.Select(p => ((long?)p.UserId, p.Name, ProtonText.Hours(p.Minutes)));

    private static IEnumerable<(long?, string, decimal)> Clients(DTO.Response.Reports.WorkTotalsResponse w)
        => w.ByClient.Select(c => (c.ClientId, c.Name ?? "Internal work", ProtonText.Hours(c.Minutes)));
}

public enum MoneyGrouping
{
    Total,
    Source,
    Project,
}

public enum MoneySort
{
    Cost,
    Received,
    Profit,
    CostChange,
}

public sealed record CompareMoneyInput
{
    [Description("design or execution. May be left out only when the user sees one of them.")]
    public ProtonDomain? Domain { get; init; }

    [Description("Default this_month.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    [Description("Default previous. none for a single period.")]
    public ProtonComparison? CompareWith { get; init; }

    public DateOnly? CompareFrom { get; init; }

    public DateOnly? CompareTo { get; init; }

    [Description("Default source: cost by labour, expenses, materials and ground labour.")]
    public MoneyGrouping? GroupBy { get; init; }

    [Description("With group_by project: cost, received, profit (cash: received − cost) or cost_change. Default cost.")]
    public MoneySort? Sort { get; init; }

    [Description("Projects to return, 1 to 15 (default 10).")]
    public int? Limit { get; init; }
}

/// <param name="ExpectedRevenue">Agreed in the period (a contract set or raised, an Execution material first sent), not a forecast.</param>
/// <param name="CashProfit">Received less cost.</param>
/// <param name="CashMarginPct">Cash profit as a whole percentage of what was received, while anything was.</param>
/// <param name="ProjectsWithMoney">Projects with money received or costs in either period; <paramref name="Projects"/> holds the first ones.</param>
/// <param name="Unconverted">Costs left out of the USD figures for want of an exchange rate, this period.</param>
/// <param name="PreviousUnconverted">The same, in the period compared with: a change can come from what each left out.</param>
public sealed record MoneyComparison(
    ProtonFigure Received,
    ProtonFigure ExpectedRevenue,
    ProtonFigure Cost,
    ProtonFigure CashProfit,
    int? CashMarginPct,
    int? PreviousCashMarginPct,
    bool CostNotable,
    bool ReceivedNotable,
    IReadOnlyList<ProtonGroupRow>? CostBySource,
    int ProjectsWithMoney,
    IReadOnlyList<MoneyProjectRow>? Projects,
    IReadOnlyList<ProtonGroupRow>? BiggestCostIncreases,
    IReadOnlyList<string> Unconverted,
    IReadOnlyList<string>? PreviousUnconverted);

public sealed record MoneyProjectRow(
    long ProjectId, string Title, ProjectStatus Status, ProtonFigure Received, ProtonFigure Cost, ProtonFigure CashProfit, bool CostNotable, bool HasUnconverted);

/// <summary>
/// A domain's money in two periods, from the Financials summary's own calculation (GET /api/finance/{domain}/summary):
/// received, cost by source, profit, and per project. Never per person.
/// </summary>
public sealed class CompareMoneyTool(ProtonAnalysisService analysis) : ProtonTool<CompareMoneyInput>
{
    private static readonly string[] Sources = ["labour", "expenses", "materials", "ground labour"];

    public override string Name => "compare_money";

    public override string Description =>
        "A domain's money in a period compared with another (the previous one by default): money received, expected " +
        "revenue (agreed in the period), cost (by source: labour, expenses, materials, ground labour), cash profit " +
        "(received − cost) and its margin, and per project with the biggest cost increases. All in USD, already " +
        "calculated. Use it for trends in money received and cost, and for which measurable costs made a change.";

    public override bool Heavy => true;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<FinanceController>(nameof(FinanceController.Summary));

    protected override async Task<ProtonToolOutput> RunAsync(CompareMoneyInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var pair = context.Compare(input.Period, input.From, input.To, ProtonPeriod.ThisMonth, input.CompareWith, input.CompareFrom, input.CompareTo);
        var limit = ProtonText.Limit(input.Limit, 10, 15);
        var t = analysis.Thresholds;
        var domain = context.DomainOf(input.Domain);
        var ranges = pair.Previous is { } previous ? new[] { pair.Current, previous } : [pair.Current];
        var money = await analysis.MoneyAsync(domain, ranges, cancellationToken);
        var now = money.Totals[0];
        var before = money.Totals.Count > 1 ? money.Totals[1] : null;

        var cost = ProtonFigure.Of(now.Cost.Total, before?.Cost.Total);
        var received = ProtonFigure.Of(now.Received, before?.Received);
        var grouping = input.GroupBy ?? MoneyGrouping.Source;

        IReadOnlyList<ProtonGroupRow>? bySource = grouping == MoneyGrouping.Source
            ? ProtonAnalysis.Rows(SourcesOf(now.Cost), before is null ? null : SourcesOf(before.Cost), t.NotableUsd, t)
            : null;

        IReadOnlyList<MoneyProjectRow>? ranked = null;
        IReadOnlyList<MoneyProjectRow>? projects = null;
        IReadOnlyList<ProtonGroupRow>? increases = null;
        var withMoney = money.Projects.Where(p => p.Periods.Any(m => m.Received != 0 || m.Cost.Total != 0)).ToList();
        if (grouping == MoneyGrouping.Project)
        {
            var rows = withMoney.Select(p =>
            {
                var a = p.Periods[0];
                var b = p.Periods.Count > 1 ? p.Periods[1] : null;
                var projectCost = ProtonFigure.Of(a.Cost.Total, b?.Cost.Total);
                return new MoneyProjectRow(
                    p.ProjectId, p.Title, p.Status,
                    ProtonFigure.Of(a.Received, b?.Received), projectCost, ProtonFigure.Of(a.Profit, b?.Profit),
                    b is not null && ProtonSignals.IsNotable(ProtonTrends.Change(a.Cost.Total, b.Cost.Total), t.NotableUsd, t),
                    a.HasUnconverted || (b?.HasUnconverted ?? false));
            }).ToList();
            IOrderedEnumerable<MoneyProjectRow> sorted = (input.Sort ?? MoneySort.Cost) switch
            {
                MoneySort.Received => rows.OrderByDescending(r => r.Received.Current),
                MoneySort.Profit => rows.OrderByDescending(r => r.CashProfit.Current),
                MoneySort.CostChange => rows.OrderByDescending(r => Math.Abs(r.Cost.Diff ?? 0)),
                _ => rows.OrderByDescending(r => r.Cost.Current),
            };
            ranked = sorted.ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
            projects = ranked.Take(limit).ToList();
            if (before is not null)
            {
                increases = ProtonAnalysis.Increases(
                    ProtonAnalysis.Rows(
                        withMoney.Select(p => ((long?)p.ProjectId, p.Title, p.Periods[0].Cost.Total)),
                        withMoney.Select(p => ((long?)p.ProjectId, p.Title, p.Periods[1].Cost.Total)),
                        t.NotableUsd, t),
                    5);
            }
        }

        var comparison = new MoneyComparison(
            received,
            ProtonFigure.Of(now.ExpectedRevenue, before?.ExpectedRevenue),
            cost,
            ProtonFigure.Of(now.Profit, before?.Profit),
            ProtonAnalysis.Pct(now.Margin),
            ProtonAnalysis.Pct(before?.Margin),
            before is not null && ProtonSignals.IsNotable(ProtonTrends.Change(cost.Current, cost.Previous!.Value), t.NotableUsd, t),
            before is not null && ProtonSignals.IsNotable(ProtonTrends.Change(received.Current, received.Previous!.Value), t.NotableUsd, t),
            bySource,
            withMoney.Count,
            projects,
            increases,
            ProtonAnalysis.Unconverted(now.Unconverted),
            before is { Unconverted.Count: > 0 } ? ProtonAnalysis.Unconverted(before.Unconverted) : null);

        var refs = (projects?.Select(p => (p.ProjectId, p.Title)) ?? [])
            .Concat(increases?.Select(r => (r.Id!.Value, r.Name)) ?? [])
            .Distinct()
            .Select(p => new ProtonRef(ProtonRefType.Project, p.Item1, p.Item2, domain))
            .ToList();
        return new ProtonToolOutput(comparison, refs, pair.Current)
        {
            Basis = ProtonAnalysis.Basis(
                $"{(domain == DomainCodes.Design ? "Design" : "Execution")} money compared",
                pair,
                $"{withMoney.Count} {domain} project{(withMoney.Count == 1 ? "" : "s")} with money in the periods",
                [
                    ProtonSignals.NotableMoneyRule(t),
                    "received: payments on the day they arrived; cost: every cost dated in the period, in USD at the rate of its day; " +
                    "cash profit: received − cost",
                ],
                comparison.Unconverted.Count > 0 || comparison.PreviousUnconverted is not null ? [ProtonAnalysis.UnconvertedLeftOut] : []),
            Datasets = Datasets(pair, money, bySource, ranked, domain),
        };
    }

    /// <summary>The comparison as tables, all in USD: the headline figures of both periods, cost by source, and each project's money.</summary>
    private static IReadOnlyList<ProtonDataset> Datasets(
        ProtonPeriodPair pair, FinanceProjectPeriodsResponse money, IReadOnlyList<ProtonGroupRow>? bySource, IReadOnlyList<MoneyProjectRow>? projects, string domain)
    {
        var now = money.Totals[0];
        var before = money.Totals.Count > 1 ? money.Totals[1] : null;
        var unconverted = new List<string>();
        if (now.Unconverted.Count > 0)
        {
            unconverted.Add($"Left out of these USD figures for want of an exchange rate on their day: {string.Join(", ", ProtonAnalysis.Unconverted(now.Unconverted))}.");
        }

        if (before is { Unconverted.Count: > 0 })
        {
            unconverted.Add($"Left out of the earlier period's figures the same way: {string.Join(", ", ProtonAnalysis.Unconverted(before.Unconverted))}.");
        }
        var usd = (string key, string label) => ProtonDatasets.Figure(key, label, ProtonUnit.Usd);
        var totals = ProtonDatasets.Compared(
            "Money compared",
            pair,
            [usd("received", "Received"), usd("expected_revenue", "Expected revenue"), usd("cost", "Cost"), usd("cash_profit", "Profit (received − cost)"),
                ProtonDatasets.Figure("margin", "Margin", ProtonUnit.Percent)],
            Figures(now),
            before is null ? null : Figures(before),
            unconverted);
        var sources = bySource is null ? null : ProtonAnalysis.Groups("Cost by source", "source", "Source", pair, bySource, ProtonUnit.Usd, "Cost", _ => null, unconverted);
        var perProject = projects is null ? null : new ProtonDataset(
            "Money by project",
            [ProtonDatasets.Label("project", "Project"), usd("received", "Received"), usd("cost", "Cost"), usd("cash_profit", "Profit (received − cost)")],
            projects.Take(ProtonDatasets.MaxRows).Select(p => ProtonDatasets.Row(
                new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, domain),
                ProtonCell.Of(p.Title), ProtonCell.Of(p.Received.Current), ProtonCell.Of(p.Cost.Current), ProtonCell.Of(p.CashProfit.Current))).ToList(),
            ProtonDatasets.Period(pair.Current),
            projects.Count,
            Notes: projects.Any(p => p.HasUnconverted) ? ["Some projects have costs in another currency with no exchange rate on their day: they're left out."] : null);
        return ProtonDatasets.WithRows(totals, sources, perProject);
    }

    private static IReadOnlyList<decimal?> Figures(FinanceDomainPeriodResponse m)
        => [m.Received, m.ExpectedRevenue, m.Cost.Total, m.Profit, ProtonAnalysis.Pct(m.Margin)];

    private static IEnumerable<(long?, string, decimal)> SourcesOf(PeriodCostResponse c)
        => [(1, Sources[0], c.Labour), (2, Sources[1], c.Expenses), (3, Sources[2], c.Materials), (4, Sources[3], c.GroundLabour)];
}
