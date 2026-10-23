using System.ComponentModel;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Activity;
using ID_Indetails.api.DTO.Request.Reports;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Analysis;
using ID_Indetails.api.Services.Activity.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Dashboard.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Requirements.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

/// <summary>One attention signal as the model reads it: what fired, the measured facts, and the rule.</summary>
public sealed record SignalRow(ProtonSignalKind Kind, string Fact, string Rule);

/// <param name="Project">Null for a domain-wide signal (people not logging).</param>
public sealed record AttentionItem(ProtonNamed? Project, string? Domain, IReadOnlyList<SignalRow> Signals);

internal static class ProtonAttention
{
    public static IReadOnlyList<AttentionItem> Items(IEnumerable<ProtonSignalGroup> groups)
        => groups.Select(g => new AttentionItem(
                g.ProjectId is { } id ? new ProtonNamed(id, g.ProjectName ?? "") : null,
                g.Domain,
                g.Signals.Select(s => new SignalRow(s.Kind, s.Fact, s.Rule)).ToList()))
            .ToList();

    public static IEnumerable<ProtonRef> Refs(IEnumerable<AttentionItem> items)
        => items.Where(i => i.Project is not null).Select(i => new ProtonRef(ProtonRefType.Project, i.Project!.Id, i.Project.Name, i.Domain));
}

public sealed record ReviewProjectInput
{
    public required long ProjectId { get; init; }

    [Description("Default this_month.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    [Description("Default previous.")]
    public ProtonComparison? CompareWith { get; init; }

    public DateOnly? CompareFrom { get; init; }

    public DateOnly? CompareTo { get; init; }
}

/// <param name="Contributors">People by hours on the project, with the change in each one's hours.</param>
public sealed record ReviewHours(ProtonFigure Hours, ProtonFigure Entries, ProtonFigure People, bool HoursNotable, IReadOnlyList<ProtonGroupRow> Contributors);

/// <param name="NextDeadline">The soonest deadline of an open requirement, today or later.</param>
public sealed record ReviewRequirements(int Total, int Done, int Open, int Overdue, DateOnly? NextDeadline);

/// <summary>The project's money: the period's cash, and all time against its agreed revenue.</summary>
/// <param name="CashProfit">The period's received less its cost.</param>
/// <param name="AllTimeAgreedRevenue">The contract value, not cash.</param>
/// <param name="AllTimeAgreedProfit">Agreed revenue less the cost so far.</param>
/// <param name="AllTimeCostOfAgreedPct">All-time cost as a whole percentage of the agreed revenue, while there is one.</param>
public sealed record ReviewMoney(
    ProtonFigure Received,
    ProtonFigure Cost,
    ProtonFigure CashProfit,
    bool CostNotable,
    decimal AllTimeAgreedRevenue,
    decimal AllTimeCost,
    decimal AllTimeAgreedProfit,
    int? AllTimeAgreedMarginPct,
    int? AllTimeCostOfAgreedPct);

/// <param name="DaysSinceLastWork">Design: days since work was last logged on it (since it started, if never).</param>
public sealed record ProjectReview(
    ProtonNamed Project,
    string Domain,
    ProjectStatus Status,
    int ProgressPct,
    DateOnly StartDate,
    DateOnly? TargetDate,
    bool Overdue,
    int? DaysOverdue,
    int? DaysSinceLastWork,
    string Owner,
    string? Client,
    ReviewHours? Hours,
    ReviewRequirements? Requirements,
    ReviewMoney? Money,
    IReadOnlyList<SignalRow> Signals);

/// <summary>"How is this project doing?" in one call, from the project, its work report totals, requirements and money.</summary>
public sealed class ReviewProjectTool(ProtonAnalysisService analysis, IProjectService projects, IRequirementService requirements, IClock clock)
    : ProtonTool<ReviewProjectInput>
{
    private static readonly ProtonEndpoint Requirements = ProtonEndpoint.Of<RequirementsController>(nameof(RequirementsController.List));

    public override string Name => "review_project";

    public override string Description =>
        "One project's state and trend in one call: status, progress, target date and overdue, days since work was last " +
        "logged, hours this period against the previous one with who contributed, requirements, money (this period's " +
        "cash: received, cost, cash profit; and all time against the agreed revenue), and the attention signals that " +
        "fire for it. Parts the user may not see are listed in basis.omitted.";

    public override bool Heavy => true;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(ReviewProjectInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var pair = context.Compare(input.Period, input.From, input.To, ProtonPeriod.ThisMonth, input.CompareWith, input.CompareFrom, input.CompareTo);
        var t = analysis.Thresholds;
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var row = (await analysis.ProjectsAsync(project.Domain, cancellationToken)).FirstOrDefault(p => p.Id == project.Id);
        var omitted = new List<string>();
        var rules = new List<string>();
        var refs = new List<ProtonRef> { new(ProtonRefType.Project, project.Id, project.Title, project.Domain) };

        ReviewHours? hours = null;
        if (project.Domain != DomainCodes.Design)
        {
            omitted.Add(ProtonAnalysisService.NoExecutionHours);
        }
        else if (await context.CanCallAsync(ProtonAnalysisService.Work))
        {
            var now = await WorkAsync(project.Id, pair.Current, context, cancellationToken);
            var before = pair.Previous is { } previous ? await WorkAsync(project.Id, previous, context, cancellationToken) : null;
            var total = ProtonFigure.Of(ProtonText.Hours(now.TotalMinutes), before is null ? null : ProtonText.Hours(before.TotalMinutes));
            var people = ProtonAnalysis.Rows(
                now.ByPerson.Select(p => ((long?)p.UserId, p.Name, ProtonText.Hours(p.Minutes))),
                before?.ByPerson.Select(p => ((long?)p.UserId, p.Name, ProtonText.Hours(p.Minutes))),
                t.NotableHours, t);
            hours = new ReviewHours(
                total,
                ProtonFigure.Of(now.EntryCount, before?.EntryCount),
                ProtonFigure.Of(now.ByPerson.Count, before?.ByPerson.Count),
                before is not null && ProtonSignals.IsNotable(ProtonTrends.Change(total.Current, total.Previous!.Value), t.NotableHours, t),
                people.Take(8).ToList());
            refs.AddRange(hours.Contributors.Select(p => new ProtonRef(ProtonRefType.Person, p.Id!.Value, p.Name)));
            rules.AddRange([ProtonSignals.NotableHoursRule(t), ProtonAnalysisService.HoursAreRecorded]);
        }
        else
        {
            omitted.Add(ProtonAnalysisService.NoHours);
        }

        ReviewRequirements? reqs = null;
        if (await context.CanCallAsync(Requirements))
        {
            var list = await requirements.ListAsync(project.Id, cancellationToken);
            reqs = new ReviewRequirements(
                list.Total, list.Done, list.Total - list.Done, list.Overdue,
                list.Items.Where(r => !r.IsDone && r.Deadline >= context.Today).Min(r => r.Deadline));
        }
        else
        {
            omitted.Add("requirements: need the requirement:view permission");
        }

        ReviewMoney? money = null;
        var signals = row is null ? [] : analysis.ProjectSignals(row, context.Today, t).ToList();
        if (await context.CanCallAsync(ProtonAnalysisService.Money))
        {
            var ranges = pair.Previous is { } previous ? new[] { pair.Current, previous } : [pair.Current];
            var period = (await analysis.MoneyAsync(project.Domain, ranges, cancellationToken)).Projects.First(p => p.ProjectId == project.Id);
            var a = period.Periods[0];
            var b = period.Periods.Count > 1 ? period.Periods[1] : null;
            var allTime = row?.Money;
            money = new ReviewMoney(
                ProtonFigure.Of(a.Received, b?.Received),
                ProtonFigure.Of(a.Cost.Total, b?.Cost.Total),
                ProtonFigure.Of(a.Profit, b?.Profit),
                b is not null && ProtonSignals.IsNotable(ProtonTrends.Change(a.Cost.Total, b.Cost.Total), t.NotableUsd, t),
                allTime?.Revenue ?? 0,
                allTime?.Cost ?? 0,
                allTime?.Profit ?? 0,
                allTime?.MarginKnown == true ? ProtonAnalysis.Pct(allTime.Margin) : null,
                allTime is { Revenue: > 0 } ? Business.Finance.MoneyAttention.UsedPct(allTime.Revenue, allTime.Cost) : null);
            if (await context.CanCallAsync(ProtonAnalysisService.MoneyAttention))
            {
                var followUps = await analysis.MoneyAttentionAsync(project.Domain, cancellationToken);
                var mine = followUps.Where(i => i.ProjectId == project.Id).ToList();
                signals.AddRange(ProtonAnalysisService.MoneySignals(mine, project.Domain));
                if (mine.Count == 0 && followUps.Count >= Business.Finance.MoneyAttention.MaxItems)
                {
                    // the domain's list was full: this project may have a follow-up that didn't make the cut
                    omitted.Add(ProtonAnalysisService.MoneyCapped(project.Domain));
                }
            }

            rules.Add(ProtonSignals.NotableMoneyRule(t));
        }
        else
        {
            omitted.Add(ProtonAnalysisService.NoMoney);
        }

        if (project.Domain == DomainCodes.Design && row is { Work: null, Status: ProjectStatus.Active })
        {
            omitted.Add(ProtonAnalysisService.NoWorkDetail);
        }

        var review = new ProjectReview(
            new ProtonNamed(project.Id, project.Title),
            project.Domain,
            project.Status,
            project.ProgressPct,
            project.StartDate,
            project.TargetDate,
            ProjectOverdue.IsOverdue(project.TargetDate, project.Status, context.Today),
            ProjectOverdue.DaysOverdue(project.TargetDate, project.Status, context.Today),
            row?.Work is { } work ? context.Today.DayNumber - (work.LastStartedAt is { } at ? clock.ToCompanyDate(at) : project.StartDate).DayNumber : null,
            project.Owner.Name,
            project.Client?.DisplayName,
            hours,
            reqs,
            money,
            signals.OrderBy(s => s.Kind).Select(s => new SignalRow(s.Kind, s.Fact, s.Rule)).ToList());
        return new ProtonToolOutput(review, refs, pair.Current)
        {
            Basis = ProtonAnalysis.Basis($"Review of {project.Title}", pair, $"project #{project.Id}", rules.Concat(signals.Select(s => s.Rule)), omitted),
        };
    }

    private Task<DTO.Response.Reports.WorkTotalsResponse> WorkAsync(long projectId, DayRange range, ProtonToolContext context, CancellationToken cancellationToken)
        => ValidatedAsync(new WorkReportQuery { From = range.From, To = range.To, ProjectId = projectId }, context, cancellationToken);

    private async Task<DTO.Response.Reports.WorkTotalsResponse> ValidatedAsync(WorkReportQuery query, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await context.ValidateAsync(query, cancellationToken);
        return await analysis.WorkAsync(query, cancellationToken);
    }
}

public sealed record CompanySummaryInput
{
    [Description("Default this_week.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    [Description("Default previous.")]
    public ProtonComparison? CompareWith { get; init; }

    public DateOnly? CompareFrom { get; init; }

    public DateOnly? CompareTo { get; init; }
}

/// <param name="StartedInPeriod">Projects whose start date falls in the period.</param>
/// <param name="CompletedInPeriod">From the activity history; null when the user can't see it.</param>
/// <param name="Idle">Design: active projects with no work for the idle rule's days; null when work details aren't visible.</param>
public sealed record ProjectsBlock(
    int Active, int OnHold, int Completed, int Archived, int StartedInPeriod, int? CompletedInPeriod, int Overdue, int TargetSoon, int? Idle, int Closeout);

public sealed record WorkBlock(ProtonFigure Hours, ProtonFigure Entries, ProtonFigure People, ProtonFigure Projects, bool HoursNotable, IReadOnlyList<ProtonGroupRow> BusiestProjects);

/// <param name="DueSoon">Open requirements of active and on-hold projects due in the next 14 days, overdue ones included.</param>
public sealed record RequirementsBlock(int DueSoon, int Overdue);

/// <summary>A domain's money in the period, cash basis.</summary>
/// <param name="CashProfit">Received less cost.</param>
public sealed record MoneyBlock(ProtonFigure Received, ProtonFigure Cost, ProtonFigure CashProfit, int? CashMarginPct, bool CostNotable);

/// <summary>A domain's part of the summary: each block only when the user may see it (see basis.omitted).</summary>
public sealed record DomainSummary(string Domain, ProjectsBlock? Projects, WorkBlock? Work, RequirementsBlock? Requirements, MoneyBlock? Money);

/// <summary>Company overhead, this month and last, per currency (no salaries).</summary>
public sealed record CompanyBlock(IReadOnlyList<OverheadMonth> Overhead);

public sealed record OverheadMonth(DateOnly Month, IReadOnlyList<CurrencyTotalResponse> Totals);

public sealed record CompanySummary(IReadOnlyList<DomainSummary> Domains, CompanyBlock? Company, IReadOnlyList<AttentionItem> Attention, int MoreAttention);

/// <summary>The company at a glance for a period, against the one before, made only of what this user may see.</summary>
public sealed class GetCompanySummaryTool(
    ProtonAnalysisService analysis,
    IActivityFeedService activity,
    IDashboardService dashboard,
    IOverheadService overhead) : ProtonTool<CompanySummaryInput>
{
    private const int AttentionItems = 5;

    /// <summary>Said when the company's overhead is left out, so a summary without it isn't read as complete.</summary>
    public const string NoOverhead = "Company overhead isn't included: it needs the company or overhead permission.";

    private static readonly ProtonEndpoint Activity = ProtonEndpoint.Of<ActivityController>(nameof(ActivityController.List));
    private static readonly ProtonEndpoint Overhead = ProtonEndpoint.Of<OverheadController>(nameof(OverheadController.Monthly));

    public override string Name => "get_company_summary";

    public override string Description =>
        "A summary of the company for a period (this week by default) against the previous one: per domain the user " +
        "sees, projects (active, on hold, started, completed, overdue, target soon, idle), Design hours with the busiest " +
        "projects, requirements due and overdue, money received, cost and cash profit (received − cost); company " +
        "overhead; and the top attention items. Blocks the user may not see are left out and named in basis.omitted.";

    public override bool Heavy => true;

    public override ProtonEndpoint Endpoint { get; } = ProtonAnalysisService.ProjectList;

    protected override async Task<ProtonToolOutput> RunAsync(CompanySummaryInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var pair = context.Compare(input.Period, input.From, input.To, ProtonPeriod.ThisWeek, input.CompareWith, input.CompareFrom, input.CompareTo);
        var t = analysis.Thresholds;
        var omitted = new List<string>();
        var rules = new List<string> { ProtonSignals.OverdueRule, ProtonSignals.TargetSoonRule(t), ProtonSignals.IdleRule(t), ProtonSignals.CloseoutRule(t) };
        var refs = new List<ProtonRef>();
        var domains = new List<DomainSummary>();

        foreach (var domain in context.Access.VisibleBusinesses)
        {
            var rows = await analysis.ProjectsAsync(domain, cancellationToken);
            var signals = rows.SelectMany(p => analysis.ProjectSignals(p, context.Today, t)).ToList();
            int? completed = null;
            if (await context.CanCallAsync(Activity))
            {
                var query = new ActivityListQuery { Domain = domain, Group = ActivityGroup.Projects, From = pair.Current.From, To = pair.Current.To };
                await context.ValidateAsync(query, cancellationToken);
                completed = (await activity.CountsAsync(query, cancellationToken)).Where(k => k.Kind == ActivityEventKinds.ProjectCompleted).Sum(k => k.Count);
            }
            else
            {
                omitted.Add("projects completed in the period: need the activity:view permission");
            }

            var workVisible = domain == DomainCodes.Design && rows.Any(p => p.Work is not null);
            var projects = new ProjectsBlock(
                rows.Count(p => p.Status == ProjectStatus.Active),
                rows.Count(p => p.Status == ProjectStatus.Hold),
                rows.Count(p => p.Status == ProjectStatus.Completed),
                rows.Count(p => p.Status == ProjectStatus.Archived),
                rows.Count(p => p.StartDate >= pair.Current.From && p.StartDate <= pair.Current.To),
                completed,
                signals.Count(s => s.Kind == ProtonSignalKind.ProjectOverdue),
                signals.Count(s => s.Kind == ProtonSignalKind.TargetSoon),
                workVisible ? signals.Count(s => s.Kind == ProtonSignalKind.Idle) : null,
                signals.Count(s => s.Kind == ProtonSignalKind.Closeout));

            WorkBlock? work = null;
            if (domain == DomainCodes.Design && await context.CanCallAsync(ProtonAnalysisService.Work))
            {
                var now = await WorkAsync(pair.Current, context, cancellationToken);
                var before = pair.Previous is { } previous ? await WorkAsync(previous, context, cancellationToken) : null;
                var hours = ProtonFigure.Of(ProtonText.Hours(now.TotalMinutes), before is null ? null : ProtonText.Hours(before.TotalMinutes));
                var busiest = ProtonAnalysis.Rows(
                    now.ByProject.Select(p => ((long?)p.ProjectId, p.Title, ProtonText.Hours(p.Minutes))),
                    before?.ByProject.Select(p => ((long?)p.ProjectId, p.Title, ProtonText.Hours(p.Minutes))),
                    t.NotableHours, t).Take(3).ToList();
                work = new WorkBlock(
                    hours,
                    ProtonFigure.Of(now.EntryCount, before?.EntryCount),
                    ProtonFigure.Of(now.ByPerson.Count, before?.ByPerson.Count),
                    ProtonFigure.Of(now.ByProject.Count, before?.ByProject.Count),
                    before is not null && ProtonSignals.IsNotable(ProtonTrends.Change(hours.Current, hours.Previous!.Value), t.NotableHours, t),
                    busiest);
                refs.AddRange(busiest.Select(p => new ProtonRef(ProtonRefType.Project, p.Id!.Value, p.Name, domain)));
                rules.AddRange([ProtonSignals.NotableHoursRule(t), ProtonAnalysisService.HoursAreRecorded]);
            }
            else
            {
                omitted.Add(domain == DomainCodes.Design ? ProtonAnalysisService.NoHours : ProtonAnalysisService.NoExecutionHours);
            }

            RequirementsBlock? requirements = null;
            if (await context.CanCallAsync(ProtonAnalysisService.Deadlines))
            {
                var deadlines = await dashboard.DeadlinesAsync(domain, cancellationToken);
                requirements = new RequirementsBlock(deadlines.Total, deadlines.Overdue);
            }
            else
            {
                omitted.Add("requirements due: need the report:view permission");
            }

            MoneyBlock? money = null;
            if (await context.CanCallAsync(ProtonAnalysisService.Money))
            {
                var ranges = pair.Previous is { } previous ? new[] { pair.Current, previous } : [pair.Current];
                var totals = (await analysis.MoneyAsync(domain, ranges, cancellationToken)).Totals;
                var a = totals[0];
                var b = totals.Count > 1 ? totals[1] : null;
                money = new MoneyBlock(
                    ProtonFigure.Of(a.Received, b?.Received),
                    ProtonFigure.Of(a.Cost.Total, b?.Cost.Total),
                    ProtonFigure.Of(a.Profit, b?.Profit),
                    ProtonAnalysis.Pct(a.Margin),
                    b is not null && ProtonSignals.IsNotable(ProtonTrends.Change(a.Cost.Total, b.Cost.Total), t.NotableUsd, t));
                rules.Add(ProtonSignals.NotableMoneyRule(t));
            }
            else
            {
                omitted.Add(ProtonAnalysisService.NoMoney);
            }

            domains.Add(new DomainSummary(domain, projects, work, requirements, money));
        }

        CompanyBlock? company = null;
        if (await context.CanCallAsync(Overhead))
        {
            company = new CompanyBlock((await overhead.MonthlyAsync(2, cancellationToken)).Select(m => new OverheadMonth(m.Month, m.Totals)).ToList());
        }
        else
        {
            omitted.Add(NoOverhead);
        }

        var scan = await analysis.SignalsAsync(context, null, cancellationToken);
        var (groups, more) = ProtonSignals.Rank(scan.Signals, AttentionItems);
        var attention = ProtonAttention.Items(groups);
        refs.AddRange(ProtonAttention.Refs(attention));

        return new ProtonToolOutput(new CompanySummary(domains, company, attention, more), refs.DistinctBy(r => (r.Type, r.Id)).ToList(), pair.Current)
        {
            Basis = ProtonAnalysis.Basis(
                "Company summary", pair, string.Join(" and ", context.Access.VisibleBusinesses), rules.Concat(scan.Rules), omitted.Concat(scan.Omitted)),
        };
    }

    private async Task<DTO.Response.Reports.WorkTotalsResponse> WorkAsync(DayRange range, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var query = new WorkReportQuery { From = range.From, To = range.To };
        await context.ValidateAsync(query, cancellationToken);
        return await analysis.WorkAsync(query, cancellationToken);
    }
}

public sealed record SummarizeChangesInput
{
    [Description("Default last7_days.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    [Description("Default previous: counts are compared with the period before. none to skip.")]
    public ProtonComparison? CompareWith { get; init; }

    public DateOnly? CompareFrom { get; init; }

    public DateOnly? CompareTo { get; init; }

    public ProtonDomain? Domain { get; init; }

    public long? ProjectId { get; init; }
}

public sealed record ChangeGroupCount(ActivityGroup Group, ProtonFigure Events);

public sealed record ChangeKindCount(string Kind, int Count);

/// <param name="By">Who did it, when known.</param>
public sealed record ProjectChange(string Kind, ProtonNamed? Project, string At, string? By);

/// <param name="ByKind">The most frequent kinds of change, at most 15.</param>
/// <param name="ProjectChanges">Projects created, put on hold, resumed, completed, archived or reopened, newest first, at most 15.</param>
public sealed record ChangeSummary(
    ProtonFigure Events,
    IReadOnlyList<ChangeGroupCount> ByGroup,
    IReadOnlyList<ChangeKindCount> ByKind,
    IReadOnlyList<ProjectChange> ProjectChanges,
    bool MoreProjectChanges);

/// <summary>What changed in a period, counted from the activity history (GET /api/activity) with the feed's own visibility.</summary>
public sealed class SummarizeChangesTool(IActivityFeedService activity, IClock clock) : ProtonTool<SummarizeChangesInput>
{
    private const int MaxKinds = 15;
    private const int MaxProjectChanges = 15;
    private const int ScanPageSize = 100;
    private const int MaxScanPages = 3;

    private static readonly HashSet<string> StatusKinds =
    [
        ActivityEventKinds.ProjectCreated, ActivityEventKinds.ProjectHold, ActivityEventKinds.ProjectResumed,
        ActivityEventKinds.ProjectCompleted, ActivityEventKinds.ProjectArchived, ActivityEventKinds.ProjectReopened,
    ];

    /// <summary>Pay is never Proton's to report, not even as a count of changes.</summary>
    private static readonly HashSet<string> Excluded = [ActivityEventKinds.PayChanged];

    public override string Name => "summarize_changes";

    public override string Description =>
        "What changed in a period (the last 7 days by default) from the activity history: how many changes of each " +
        "group and kind (work, money, billing, projects, people, company), against the period before, and the projects " +
        "created, put on hold, resumed, completed, archived or reopened. Filter by domain or project.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ActivityController>(nameof(ActivityController.List));

    protected override async Task<ProtonToolOutput> RunAsync(SummarizeChangesInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var pair = context.Compare(input.Period, input.From, input.To, ProtonPeriod.Last7Days, input.CompareWith, input.CompareFrom, input.CompareTo);
        var now = await CountsAsync(input, pair.Current, context, cancellationToken);
        var before = pair.Previous is { } previous ? await CountsAsync(input, previous, context, cancellationToken) : null;

        var groups = now.Select(k => k.Group).Union(before?.Select(k => k.Group) ?? [])
            .Select(g => new ChangeGroupCount(g, ProtonFigure.Of(now.Where(k => k.Group == g).Sum(k => k.Count), before?.Where(k => k.Group == g).Sum(k => k.Count))))
            .OrderByDescending(g => g.Events.Current)
            .ToList();

        var changes = new List<ProjectChange>();
        var more = false;
        for (var page = 1; page <= MaxScanPages && changes.Count <= MaxProjectChanges; page++)
        {
            var query = Query(input, pair.Current) with { Group = ActivityGroup.Projects, Page = page, PageSize = ScanPageSize };
            var result = await activity.ListAsync(query, cancellationToken);
            changes.AddRange(result.Items
                .Where(e => StatusKinds.Contains(e.Kind))
                .Select(e => new ProjectChange(
                    e.Kind, e.Project is { } p ? new ProtonNamed(p.Id, p.Name) : null, clock.Local(e.OccurredAt), e.Actor?.Name)));
            if (page * ScanPageSize >= result.TotalCount)
            {
                break;
            }

            more = page == MaxScanPages;
        }

        more |= changes.Count > MaxProjectChanges;
        var shown = changes.Take(MaxProjectChanges).ToList();
        var summary = new ChangeSummary(
            ProtonFigure.Of(now.Sum(k => k.Count), before?.Sum(k => k.Count)),
            groups,
            now.Take(MaxKinds).Select(k => new ChangeKindCount(k.Kind, k.Count)).ToList(),
            shown,
            more);
        var refs = shown.Where(c => c.Project is not null).Select(c => new ProtonRef(ProtonRefType.Project, c.Project!.Id, c.Project.Name)).DistinctBy(r => r.Id).ToList();
        var omitted = new List<string> { "changes the user can't see in the activity feed (other domains, work without work_entry:view_all, company-level without company:view)" };
        return new ProtonToolOutput(summary, refs, pair.Current)
        {
            Basis = ProtonAnalysis.Basis(
                "Changes", pair, input.ProjectId is { } id ? $"project #{id}" : input.Domain?.Code() ?? "every domain the user sees",
                ["counted from the activity history: each recorded change is one event"], omitted),
        };
    }

    private async Task<IReadOnlyList<DTO.Response.Activity.ActivityKindCountResponse>> CountsAsync(
        SummarizeChangesInput input, DayRange range, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var query = Query(input, range);
        await context.ValidateAsync(query, cancellationToken);
        return (await activity.CountsAsync(query, cancellationToken)).Where(k => !Excluded.Contains(k.Kind)).ToList();
    }

    private static ActivityListQuery Query(SummarizeChangesInput input, DayRange range)
        => new() { Domain = input.Domain?.Code(), ProjectId = input.ProjectId, From = range.From, To = range.To };
}

public sealed record AttentionInput
{
    [Description("Only this domain; every domain the user sees when not given.")]
    public ProtonDomain? Domain { get; init; }

    [Description("Items to return, 1 to 10 (default 8).")]
    public int? Limit { get; init; }
}

/// <param name="More">How many more projects had signals than are listed.</param>
public sealed record AttentionList(IReadOnlyList<AttentionItem> Items, int More);

/// <summary>
/// "What should I look at?": deterministic signals from the approved and existing rules, one entry per project with
/// all of its signals, most pressing first. Nothing here is sent to anyone.
/// </summary>
public sealed class GetAttentionTool(ProtonAnalysisService analysis) : ProtonTool<AttentionInput>
{
    public override string Name => "get_attention";

    public override string Description =>
        "What may need attention now: overdue invoices and projects, overdue requirements, costs past 85% of the " +
        "agreed revenue, target dates coming up, notable or unusual changes in weekly hours or monthly cost, idle projects, " +
        "projects close to done, on hold, and Design employees not logging this week. Each item lists the measured " +
        "facts and the rule that fired, most pressing first. Use it for 'what should I look at?'.";

    public override bool Heavy => true;

    public override ProtonEndpoint Endpoint { get; } = ProtonAnalysisService.ProjectList;

    protected override async Task<ProtonToolOutput> RunAsync(AttentionInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var scan = await analysis.SignalsAsync(context, input.Domain?.Code(), cancellationToken);
        var (groups, more) = ProtonSignals.Rank(scan.Signals, ProtonText.Limit(input.Limit, 8, 10));
        var items = ProtonAttention.Items(groups);
        var today = new ProtonPeriodPair(new DayRange(context.Today, context.Today), null, false, null);
        return new ProtonToolOutput(new AttentionList(items, more), ProtonAttention.Refs(items).ToList())
        {
            Basis = ProtonAnalysis.Basis(
                "What may need attention", today, input.Domain?.Code() ?? string.Join(" and ", context.Access.VisibleBusinesses), scan.Rules, scan.Omitted),
        };
    }
}
