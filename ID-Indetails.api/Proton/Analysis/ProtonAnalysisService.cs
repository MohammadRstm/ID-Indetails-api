using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Finance;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Request.Reports;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.DTO.Response.Reports;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Dashboard.Abstraction;
using ID_Indetails.api.Services.MoneySummaries.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Reports.Abstraction;
using ID_Indetails.api.Services.Reports.Implementation;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Analysis;

/// <summary>
/// The data Proton's analytical tools share, read through the existing services (so their permission and domain checks
/// apply) and kept for the rest of the question: a summary followed by a project review loads each domain's money once.
/// It lives for one request, one user, so nothing it keeps can go stale or reach anyone else.
/// </summary>
public sealed class ProtonAnalysisService(
    IReportService reports,
    IDomainFinanceService finance,
    IProjectService projects,
    IDashboardService dashboard,
    IClock clock,
    IOptions<ProtonOptions> options)
{
    public static readonly ProtonEndpoint ProjectList = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));
    public static readonly ProtonEndpoint Work = ProtonEndpoint.Of<ReportsController>(nameof(ReportsController.Work));
    public static readonly ProtonEndpoint Overview = ProtonEndpoint.Of<ReportsController>(nameof(ReportsController.Overview));
    public static readonly ProtonEndpoint Money = ProtonEndpoint.Of<FinanceController>(nameof(FinanceController.Summary));
    public static readonly ProtonEndpoint MoneyAttention = ProtonEndpoint.Of<FinanceController>(nameof(FinanceController.Attention));
    public static readonly ProtonEndpoint Deadlines = ProtonEndpoint.Of<DashboardController>(nameof(DashboardController.Deadlines));

    public const string NoMoney = "money: needs the financial:view permission";
    public const string NoHours = "hours: need the report:view permission";
    public const string NoProjects = "projects: need the project:view permission";
    public const string NoWorkDetail = "idle projects and weekly hours: need the work_entry:view_all permission";
    public const string NoExecutionHours = "hours: Execution doesn't log hours";
    public const string HoursAreRecorded = "hours are the time people recorded, which isn't necessarily all the work done";

    private const int PageSize = 100;
    private const int MaxPages = 20;

    private readonly Dictionary<WorkReportQuery, WorkTotalsResponse> _work = [];
    private readonly Dictionary<string, FinanceProjectPeriodsResponse> _money = [];
    private readonly Dictionary<string, IReadOnlyList<ProjectSummaryResponse>> _projects = [];
    private readonly Dictionary<string, IReadOnlyList<MoneyAttentionResponse>> _moneyAttention = [];

    public ProtonThresholds Thresholds { get; } = ThresholdsOf(options.Value.Insights);

    public static ProtonThresholds ThresholdsOf(ProtonInsightOptions o) => new(
        o.NotablePct, o.NotableHours, o.NotableUsd, o.UnusualHigh, o.UnusualLow, o.BaselinePeriods, o.TargetSoonDays,
        ReportService.IdleDays, ReportService.CloseoutProgress);

    public async Task<WorkTotalsResponse> WorkAsync(WorkReportQuery query, CancellationToken cancellationToken)
    {
        if (!_work.TryGetValue(query, out var totals))
        {
            totals = await reports.WorkTotalsAsync(query, cancellationToken);
            _work[query] = totals;
        }

        return totals;
    }

    public async Task<FinanceProjectPeriodsResponse> MoneyAsync(string domain, IReadOnlyList<DayRange> ranges, CancellationToken cancellationToken)
    {
        var key = $"{domain}:{string.Join(',', ranges.Select(r => $"{r.From:yyyyMMdd}-{r.To:yyyyMMdd}"))}";
        if (!_money.TryGetValue(key, out var money))
        {
            money = await finance.ProjectPeriodsAsync(
                domain, ranges.Select(r => new FinancePeriodQuery { From = r.From, To = r.To }).ToList(), cancellationToken);
            _money[key] = money;
        }

        return money;
    }

    /// <summary>Every project of the domain the caller can see, as the project list shows them (work and money as they may see).</summary>
    public async Task<IReadOnlyList<ProjectSummaryResponse>> ProjectsAsync(string domain, CancellationToken cancellationToken)
    {
        if (!_projects.TryGetValue(domain, out var all))
        {
            var rows = new List<ProjectSummaryResponse>();
            for (var page = 1; page <= MaxPages; page++)
            {
                var result = await projects.ListAsync(new ProjectListQuery { Domain = domain, Page = page, PageSize = PageSize }, cancellationToken);
                rows.AddRange(result.Items);
                if (page * PageSize >= result.TotalCount)
                {
                    break;
                }
            }

            all = rows;
            _projects[domain] = all;
        }

        return all;
    }

    public async Task<IReadOnlyList<MoneyAttentionResponse>> MoneyAttentionAsync(string domain, CancellationToken cancellationToken)
    {
        if (!_moneyAttention.TryGetValue(domain, out var items))
        {
            items = await finance.AttentionAsync(domain, cancellationToken);
            _moneyAttention[domain] = items;
        }

        return items;
    }

    /// <summary>
    /// Everything that may need attention in the domains asked for (all the caller sees when null), from data the
    /// caller may read; what couldn't be checked is listed in Omitted.
    /// </summary>
    public async Task<ProtonSignalScan> SignalsAsync(ProtonToolContext context, string? domain, CancellationToken cancellationToken)
    {
        var t = Thresholds;
        var signals = new List<ProtonSignal>();
        var omitted = new List<string>();
        var rules = new List<string>
        {
            ProtonSignals.OverdueRule, ProtonSignals.TargetSoonRule(t), ProtonSignals.CloseoutRule(t),
        };
        var domains = domain is null ? context.Access.VisibleDomains.ToList() : [domain];

        foreach (var code in domains)
        {
            if (await context.CanCallAsync(ProjectList))
            {
                var rows = await ProjectsAsync(code, cancellationToken);
                signals.AddRange(rows.SelectMany(p => ProjectSignals(p, context.Today, t)));
                if (code == DomainCodes.Design && rows.Any(p => p.Status == ProjectStatus.Active) && rows.All(p => p.Work is null))
                {
                    omitted.Add(NoWorkDetail);
                }
            }
            else
            {
                omitted.Add(NoProjects);
            }

            if (await context.CanCallAsync(MoneyAttention))
            {
                signals.AddRange(MoneySignals(await MoneyAttentionAsync(code, cancellationToken), code));
                signals.AddRange(await CostChangeSignalsAsync(code, context.Today, cancellationToken));
            }
            else
            {
                omitted.Add(NoMoney);
            }

            if (await context.CanCallAsync(Deadlines))
            {
                var deadlines = await dashboard.DeadlinesAsync(code, cancellationToken);
                signals.AddRange(deadlines.Items
                    .Where(d => d.IsOverdue)
                    .GroupBy(d => d.Project.Id)
                    .Select(g => new ProtonSignal(
                        ProtonSignalKind.OverdueRequirements, g.Key, g.First().Project.Title, code, g.Count(),
                        $"{g.Count()} open requirement{(g.Count() == 1 ? " is" : "s are")} past {(g.Count() == 1 ? "its" : "their")} deadline (oldest {g.Min(d => d.Deadline):yyyy-MM-dd})",
                        "an open requirement of an active or on-hold project past its deadline")));
                if (deadlines.Overdue > deadlines.Items.Count(d => d.IsOverdue))
                {
                    omitted.Add($"overdue requirements: {deadlines.Overdue} in {code}, only the soonest are listed per project");
                }
            }
        }

        if (domains.Contains(DomainCodes.Design) && await context.CanCallAsync(Overview))
        {
            var overview = await reports.OverviewAsync(new OverviewQuery { Period = OverviewPeriod.Month }, cancellationToken);
            if (overview.Attention.FirstOrDefault(a => a.Kind == AttentionKind.NotLogging) is { Count: > 0 } notLogging)
            {
                var names = string.Join(", ", notLogging.People?.Select(p => p.Name) ?? []);
                signals.Add(new ProtonSignal(
                    ProtonSignalKind.NotLogging, null, null, DomainCodes.Design, notLogging.Count!.Value,
                    $"{notLogging.Count} Design employee{(notLogging.Count == 1 ? " has" : "s have")} logged no work this week{(names.Length > 0 ? $" ({names}{(notLogging.Count > 3 ? ", …" : "")})" : "")}",
                    "Design employees with no work logged this week; " + HoursAreRecorded));
            }
        }

        if (signals.Any(s => s.Kind is ProtonSignalKind.Idle))
        {
            rules.Add(ProtonSignals.IdleRule(t));
        }

        if (signals.Any(s => s.Kind is ProtonSignalKind.HoursChange or ProtonSignalKind.HoursUnusual))
        {
            rules.Add("weekly hours: last full week against the week before; " + ProtonSignals.NotableHoursRule(t) + "; unusual: " + ProtonSignals.UnusualRule(t));
        }

        if (signals.Any(s => s.Kind is ProtonSignalKind.CostChange))
        {
            rules.Add("cost: the last 30 days against the 30 before; " + ProtonSignals.NotableMoneyRule(t));
        }

        if (signals.Any(s => s.Kind is ProtonSignalKind.CostHigh))
        {
            rules.Add(ProtonSignals.CostHighRule);
        }

        return new ProtonSignalScan(signals, rules, omitted.Distinct().ToList());
    }

    /// <summary>The signals of one project from what the project list shows of it.</summary>
    public IEnumerable<ProtonSignal> ProjectSignals(ProjectSummaryResponse p, DateOnly today, ProtonThresholds t)
    {
        ProtonSignal Signal(ProtonSignalKind kind, decimal weight, string fact, string rule) => new(kind, p.Id, p.Title, p.Domain, weight, fact, rule);

        if (ProjectOverdue.IsOverdue(p.TargetDate, p.Status, today))
        {
            var days = ProjectOverdue.DaysOverdue(p.TargetDate, p.Status, today) ?? 0;
            yield return Signal(ProtonSignalKind.ProjectOverdue, days, $"target date {p.TargetDate:yyyy-MM-dd} passed {days} day{(days == 1 ? "" : "s")} ago", ProtonSignals.OverdueRule);
        }
        else if (ProtonSignals.IsTargetSoon(p.TargetDate, p.Status, today, t))
        {
            var left = p.TargetDate!.Value.DayNumber - today.DayNumber;
            yield return Signal(ProtonSignalKind.TargetSoon, t.TargetSoonDays - left, $"target date {p.TargetDate:yyyy-MM-dd} is in {left} day{(left == 1 ? "" : "s")}, at {p.ProgressPct}% progress", ProtonSignals.TargetSoonRule(t));
        }

        if (p.Status == ProjectStatus.Hold)
        {
            yield return Signal(ProtonSignalKind.OnHold, 0, "on hold", "a project on hold");
        }

        if (ProtonSignals.IsCloseout(p.Status, p.ProgressPct, t))
        {
            yield return Signal(ProtonSignalKind.Closeout, p.ProgressPct, $"at {p.ProgressPct}% progress and still active", ProtonSignals.CloseoutRule(t));
        }

        if (p.Work is not { } work || p.Status != ProjectStatus.Active)
        {
            yield break;
        }

        var last = work.LastStartedAt is { } at ? clock.ToCompanyDate(at) : p.StartDate;
        var idle = today.DayNumber - last.DayNumber;
        if (ProtonSignals.IsIdle(idle, t))
        {
            yield return Signal(ProtonSignalKind.Idle, idle,
                work.LastStartedAt is null ? $"no work logged since it started {idle} days ago" : $"no work logged for {idle} days (last on {last:yyyy-MM-dd})",
                ProtonSignals.IdleRule(t));
        }

        // Weekly: 8 weeks ending with this one; the last full week is the one before this.
        if (work.Weekly.Count >= t.BaselinePeriods + 2)
        {
            var weeks = work.Weekly.Select(m => ProtonText.Hours(m)).ToList();
            var lastFull = weeks[^2];
            var change = ProtonTrends.Change(lastFull, weeks[^3]);
            var baseline = ProtonTrends.Baseline(weeks.Take(weeks.Count - 2).ToList(), t.BaselinePeriods);
            if (ProtonSignals.IsUnusual(lastFull, baseline, t.NotableHours, t))
            {
                yield return Signal(ProtonSignalKind.HoursUnusual, Math.Abs(lastFull - baseline!.Value),
                    $"{lastFull:0.#} h last week against an average of {baseline:0.#} h over the {t.BaselinePeriods} weeks before", ProtonSignals.UnusualRule(t));
            }
            else if (ProtonSignals.IsNotable(change, t.NotableHours, t))
            {
                yield return Signal(ProtonSignalKind.HoursChange, Math.Abs(change.Diff),
                    $"{lastFull:0.#} h last week against {change.Previous:0.#} h the week before ({Signed(change)})", ProtonSignals.NotableHoursRule(t));
            }
        }
    }

    public static IEnumerable<ProtonSignal> MoneySignals(IEnumerable<MoneyAttentionResponse> items, string domain)
        => items.Select(i => i.Kind switch
        {
            MoneyAttentionKind.OverdueInvoice => new ProtonSignal(
                ProtonSignalKind.OverdueInvoice, i.ProjectId, i.ProjectTitle, domain, i.Amount ?? 0,
                $"invoice {i.InvoiceNumber ?? "(no number)"}: {i.Amount:0.00} USD overdue by {i.DaysOverdue} days", "an issued invoice past its due date"),
            MoneyAttentionKind.FinishedOwed => new ProtonSignal(
                ProtonSignalKind.FinishedOwed, i.ProjectId, i.ProjectTitle, domain, i.Amount ?? 0,
                $"finished, with {i.Amount:0.00} USD still owed on its invoices", "a completed or archived project still owed on its invoices"),
            MoneyAttentionKind.NoPayment => new ProtonSignal(
                ProtonSignalKind.NoPayment, i.ProjectId, i.ProjectTitle, domain, 0,
                "an active client project with no payment agreed", "an active client project with no payment agreed"),
            _ => new ProtonSignal(
                ProtonSignalKind.CostHigh, i.ProjectId, i.ProjectTitle, domain, i.UsedPct ?? 0,
                $"cost {i.Amount:0.00} USD is {i.UsedPct}% of the {i.Revenue:0.00} USD payment", ProtonSignals.CostHighRule),
        });

    private async Task<IEnumerable<ProtonSignal>> CostChangeSignalsAsync(string domain, DateOnly today, CancellationToken cancellationToken)
    {
        var t = Thresholds;
        var money = await MoneyAsync(domain, [new DayRange(today.AddDays(-29), today), new DayRange(today.AddDays(-59), today.AddDays(-30))], cancellationToken);
        return money.Projects
            .Select(p => (Project: p, Change: ProtonTrends.Change(p.Periods[0].Cost.Total, p.Periods[1].Cost.Total)))
            .Where(x => ProtonSignals.IsNotable(x.Change, t.NotableUsd, t))
            .Select(x => new ProtonSignal(
                ProtonSignalKind.CostChange, x.Project.ProjectId, x.Project.Title, domain, Math.Abs(x.Change.Diff),
                $"cost {x.Change.Current:0.00} USD in the last 30 days against {x.Change.Previous:0.00} USD the 30 before ({Signed(x.Change)})",
                ProtonSignals.NotableMoneyRule(t)));
    }

    public static string Signed(ProtonChange change)
        => change.Pct is { } pct ? $"{(pct >= 0 ? "+" : "")}{pct}%" : change.Diff == 0 ? "no change" : "from none";
}

public sealed record ProtonSignalScan(IReadOnlyList<ProtonSignal> Signals, IReadOnlyList<string> Rules, IReadOnlyList<string> Omitted);
