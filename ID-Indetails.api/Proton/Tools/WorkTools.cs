using System.ComponentModel;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Reports;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Reports;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Reports.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record WorkEntriesInput
{
    public long? UserId { get; init; }

    public long? ProjectId { get; init; }

    public long? ClientId { get; init; }

    [Description("Default this_week.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    [Description("Words that must appear in the description or project title.")]
    public string? Search { get; init; }

    [Description("Entries to return, 1 to 25 (default 20), newest first.")]
    public int? Limit { get; init; }
}

public sealed record WorkEntryRow(
    long Id, ProtonNamed Person, ProtonNamed Project, string Started, string Ended, int Minutes, int OvertimeMinutes, string Description);

public sealed record WorkEntryList(DateOnly From, DateOnly To, int Total, int Shown, IReadOnlyList<WorkEntryRow> Entries);

/// <summary>Logged work with its descriptions, as GET /api/work-entries returns it. Design only: Execution logs no hours.</summary>
public sealed class ListWorkEntriesTool(IWorkEntryQueryService workEntries, IClock clock) : ProtonTool<WorkEntriesInput>
{
    public override string Name => "list_work_entries";

    public override string Description =>
        "What people actually worked on: logged work entries with their descriptions, newest first, for a person, " +
        "project or client in a period. Design only.";

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<WorkEntriesController>(nameof(WorkEntriesController.List));

    protected override async Task<ProtonToolOutput> RunAsync(WorkEntriesInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var range = context.Period(input.Period, input.From, input.To, ProtonPeriod.ThisWeek);
        var query = new WorkEntryListQuery
        {
            From = range.From,
            To = range.To,
            UserId = input.UserId,
            ProjectId = input.ProjectId,
            ClientId = input.ClientId,
            Search = input.Search,
            PageSize = ProtonText.Limit(input.Limit, 20, 25),
        };
        await context.ValidateAsync(query, cancellationToken);
        var page = await workEntries.ListAsync(query, cancellationToken);

        var rows = page.Items.Select(e => new WorkEntryRow(
            e.Id,
            new ProtonNamed(e.Person.Id, e.Person.Name),
            new ProtonNamed(e.Project.Id, e.Project.Title),
            clock.Local(e.StartedAt),
            clock.Local(e.EndedAt),
            e.Minutes,
            e.OvertimeMinutes,
            ProtonText.Cut(e.Description, 400)!)).ToList();
        var refs = rows.Select(r => new ProtonRef(ProtonRefType.Person, r.Person.Id, r.Person.Name))
            .Concat(rows.Select(r => new ProtonRef(ProtonRefType.Project, r.Project.Id, r.Project.Name, DomainCodes.Design)))
            .Distinct()
            .ToList();
        return new ProtonToolOutput(new WorkEntryList(range.From, range.To, page.TotalCount, rows.Count, rows), refs, range);
    }
}

public sealed record TimeSummaryInput
{
    [Description("Default this_month.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public long? ProjectId { get; init; }

    public long? UserId { get; init; }

    public long? ClientId { get; init; }

    [Description("Only internal (non-client) projects.")]
    public bool InternalOnly { get; init; }
}

public sealed record TimeByProject(long ProjectId, string Title, ProjectStatus Status, decimal Hours, int Entries, int People, int SharePct);

public sealed record TimeByPerson(long UserId, string Name, decimal Hours, decimal OvertimeHours, int Entries);

public sealed record TimeByClient(long? ClientId, string? Name, decimal Hours);

public sealed record TimeWeek(DateOnly WeekStart, decimal Hours, int Entries);

public sealed record TimeSummary(
    DateOnly From,
    DateOnly To,
    decimal Hours,
    decimal OvertimeHours,
    int Entries,
    int Projects,
    int People,
    TimeWeek? BusiestWeek,
    IReadOnlyList<TimeWeek> Weekly,
    IReadOnlyList<TimeByProject> ByProject,
    IReadOnlyList<TimeByPerson> ByPerson,
    IReadOnlyList<TimeByClient> ByClient);

/// <summary>Hours added up for a period, as GET /api/reports/work computes them. Labour cost per person is left out.</summary>
public sealed class GetTimeSummaryTool(IReportService reports) : ProtonTool<TimeSummaryInput>
{
    private const int Top = 15;

    public override string Name => "get_time_summary";

    public override string Description =>
        "Hours logged in a period, totalled by project, by person and by client, with weekly totals. Filters: project, " +
        "person, client. Use it for 'who worked most', 'which project took most time', 'compare months'. Design only.";

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ReportsController>(nameof(ReportsController.Work));

    protected override async Task<ProtonToolOutput> RunAsync(TimeSummaryInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var range = context.Period(input.Period, input.From, input.To, ProtonPeriod.ThisMonth);
        var query = new WorkReportQuery
        {
            From = range.From,
            To = range.To,
            ProjectId = input.ProjectId,
            UserId = input.UserId,
            ClientId = input.ClientId,
            Internal = input.InternalOnly,
        };
        await context.ValidateAsync(query, cancellationToken);
        var report = await reports.WorkReportAsync(query, cancellationToken);

        var byProject = report.ByProject.Take(Top)
            .Select(p => new TimeByProject(p.ProjectId, p.Title, p.Status, ProtonText.Hours(p.Minutes), p.Entries, p.People, p.SharePct))
            .ToList();
        var byPerson = report.ByPerson.Take(Top)
            .Select(p => new TimeByPerson(p.UserId, p.Name, ProtonText.Hours(p.Minutes), ProtonText.Hours(p.OvertimeMinutes), p.Entries))
            .ToList();
        var summary = new TimeSummary(
            report.From, report.To, ProtonText.Hours(report.TotalMinutes), ProtonText.Hours(report.OvertimeMinutes),
            report.EntryCount, report.ProjectCount, report.PeopleCount,
            report.BusiestWeek is { } busiest ? Week(busiest) : null,
            report.Weekly.Select(Week).ToList(),
            byProject,
            byPerson,
            report.ByClient.Take(10).Select(c => new TimeByClient(c.ClientId, c.Name, ProtonText.Hours(c.Minutes))).ToList());

        var refs = byProject.Select(p => new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, DomainCodes.Design))
            .Concat(byPerson.Select(p => new ProtonRef(ProtonRefType.Person, p.UserId, p.Name)))
            .ToList();
        return new ProtonToolOutput(summary, refs, new DayRange(report.From, report.To));
    }

    private static TimeWeek Week(DTO.Response.WorkEntries.WeekTotalResponse week)
        => new(week.WeekStart, ProtonText.Hours(week.Minutes), week.Entries);
}

public enum ProtonOverviewPeriod
{
    Month,
    Quarter,
    Last30Days,
}

public sealed record WorkOverviewInput
{
    [Description("month or quarter to date, or the last 30 days. Default month.")]
    public ProtonOverviewPeriod? Period { get; init; }
}

public sealed record ProjectHours(long ProjectId, string Title, decimal Hours);

public sealed record AttentionRow(AttentionKind Kind, ProtonNamed? Project, int? Days, int? ProgressPct, int? Count, IReadOnlyList<string>? People);

public sealed record WorkOverview(
    DateOnly From,
    DateOnly To,
    DateOnly PreviousFrom,
    DateOnly PreviousTo,
    decimal Hours,
    decimal PreviousHours,
    decimal OvertimeHours,
    int Entries,
    int PreviousEntries,
    decimal ThisWeekHours,
    int PeopleLoggingThisWeek,
    int People,
    ProtonNamed? BusiestProjectThisWeek,
    IReadOnlyList<AttentionRow> NeedsAttention,
    IReadOnlyList<ProjectHours> TopProjects,
    IReadOnlyList<StatusCountResponse> ProjectsByStatus,
    string Note);

/// <summary>The Design dashboard's overview, as GET /api/reports/overview returns it.</summary>
public sealed class GetWorkOverviewTool(IReportService reports) : ProtonTool<WorkOverviewInput>
{
    public override string Name => "get_work_overview";

    public override string Description =>
        "The Design studio at a glance: hours this period against the previous one, who is logging this week, the " +
        "busiest project, and what needs attention (idle projects, projects close to done, on hold, people not logging).";

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ReportsController>(nameof(ReportsController.Overview));

    protected override async Task<ProtonToolOutput> RunAsync(WorkOverviewInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var query = new OverviewQuery
        {
            Period = input.Period switch
            {
                ProtonOverviewPeriod.Quarter => OverviewPeriod.Quarter,
                ProtonOverviewPeriod.Last30Days => OverviewPeriod.Last30,
                _ => OverviewPeriod.Month,
            },
        };
        await context.ValidateAsync(query, cancellationToken);
        var o = await reports.OverviewAsync(query, cancellationToken);

        var overview = new WorkOverview(
            o.From, o.To, o.PreviousFrom, o.PreviousTo,
            ProtonText.Hours(o.Minutes), ProtonText.Hours(o.PreviousMinutes), ProtonText.Hours(o.OvertimeMinutes),
            o.Entries, o.PreviousEntries, ProtonText.Hours(o.ThisWeekMinutes), o.PeopleLoggingThisWeek, o.People,
            o.BusiestProjectThisWeek is { } b ? new ProtonNamed(b.ProjectId, b.Title) : null,
            o.Attention.Select(a => new AttentionRow(
                a.Kind,
                a.ProjectId is { } id ? new ProtonNamed(id, a.Title ?? "") : null,
                a.Days, a.ProgressPct, a.Count, a.People?.Select(p => p.Name).ToList())).ToList(),
            o.ByProject.Select(p => new ProjectHours(p.ProjectId, p.Title, ProtonText.Hours(p.Minutes))).ToList(),
            o.ProjectsByStatus,
            "The previous period is the same span just before. Not-logging lists at most three names.");
        return new ProtonToolOutput(
            overview,
            o.ByProject.Select(p => new ProtonRef(ProtonRefType.Project, p.ProjectId, p.Title, DomainCodes.Design)).ToList(),
            new DayRange(o.From, o.To));
    }
}
