using System.ComponentModel;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Services.Execution.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Requirements.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record ProjectIdInput
{
    public required long ProjectId { get; init; }
}

public sealed record ListProjectsInput
{
    public ProjectStatus? Status { get; init; }

    public ProtonDomain? Domain { get; init; }

    public long? ClientId { get; init; }

    [Description("The person who owns (is responsible for) the projects.")]
    public long? OwnerId { get; init; }

    [Description("Words of the title; every word must match.")]
    public string? Search { get; init; }

    [Description("Only active or on-hold projects whose target date has passed. Projects without a target date are never overdue.")]
    public bool OverdueOnly { get; init; }

    [Description("Projects open at some point during this year (and quarter).")]
    public int? Year { get; init; }

    [Description("1 to 4; needs year.")]
    public int? Quarter { get; init; }

    [Description("start (newest first, the default), activity (latest work first), hours (most logged), progress, name or client.")]
    public ProjectSort? Sort { get; init; }

    [Description("Rows to return, 1 to 25 (default 15).")]
    public int? Limit { get; init; }
}

public sealed record ProjectMoneyRow(
    decimal Revenue, decimal Cost, decimal Profit, decimal? Margin, bool MarginKnown, decimal Invoiced, decimal Outstanding, bool HasUnconverted);

public sealed record ProjectWorkRow(decimal Hours, decimal HoursLast14Days, int Contributors, DateTime? LastWorkAt);

public sealed record ProjectRow(
    long Id,
    string Title,
    string Domain,
    ProjectStatus Status,
    string? Client,
    string Owner,
    DateOnly StartDate,
    DateOnly? TargetDate,
    bool Overdue,
    int? DaysOverdue,
    int ProgressPct,
    string? Location,
    ProjectWorkRow? Work,
    ProjectMoneyRow? Money);

public sealed record ProjectList(int Total, int Shown, IReadOnlyList<ProjectRow> Projects);

/// <summary>The project list, as GET /api/projects returns it, plus the overdue rule.</summary>
public sealed class ListProjectsTool(IProjectService projects) : ProtonTool<ListProjectsInput>
{
    private const int ScanPageSize = 100;
    private const int MaxScanPages = 5;

    public override string Name => "list_projects";

    public override string Description =>
        "List and count projects with filters (status, domain, client, owner, title words, year/quarter, overdue). " +
        "Rows carry hours and money only when this user may see them.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.List));

    protected override async Task<ProtonToolOutput> RunAsync(ListProjectsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var limit = ProtonText.Limit(input.Limit, 15, 25);
        var query = new ProjectListQuery
        {
            Search = input.Search,
            Status = input.Status,
            ClientId = input.ClientId,
            Domain = input.Domain.Code(),
            OwnerId = input.OwnerId,
            Year = input.Year,
            Quarter = input.Quarter,
            Sort = input.Sort ?? ProjectSort.Start,
            PageSize = input.OverdueOnly ? ScanPageSize : limit,
        };
        await context.ValidateAsync(query, cancellationToken);

        List<ProjectSummaryResponse> rows;
        int total;
        if (input.OverdueOnly)
        {
            rows = await OverdueAsync(query, context.Today, cancellationToken);
            total = rows.Count;
        }
        else
        {
            var page = await projects.ListAsync(query, cancellationToken);
            rows = page.Items.ToList();
            total = page.TotalCount;
        }

        var shown = rows.Take(limit).Select(p => Row(p, context.Today)).ToList();
        return new ProtonToolOutput(
            new ProjectList(total, shown.Count, shown),
            shown.Select(p => new ProtonRef(ProtonRefType.Project, p.Id, p.Title, p.Domain)).ToList());
    }

    /// <summary>Overdue projects are active or on hold, so only those two statuses are read, a few pages at most.</summary>
    private async Task<List<ProjectSummaryResponse>> OverdueAsync(ProjectListQuery query, DateOnly today, CancellationToken cancellationToken)
    {
        ProjectStatus[] statuses = query.Status is { } asked
            ? asked is ProjectStatus.Active or ProjectStatus.Hold ? [asked] : []
            : [ProjectStatus.Active, ProjectStatus.Hold];
        var overdue = new List<ProjectSummaryResponse>();
        foreach (var status in statuses)
        {
            for (var page = 1; page <= MaxScanPages; page++)
            {
                var result = await projects.ListAsync(query with { Status = status, Page = page }, cancellationToken);
                overdue.AddRange(result.Items.Where(p => ProjectOverdue.IsOverdue(p.TargetDate, p.Status, today)));
                if (page * ScanPageSize >= result.TotalCount)
                {
                    break;
                }
            }
        }

        return overdue.OrderByDescending(p => ProjectOverdue.DaysOverdue(p.TargetDate, p.Status, today)).ToList();
    }

    private static ProjectRow Row(ProjectSummaryResponse p, DateOnly today) => new(
        p.Id,
        p.Title,
        p.Domain,
        p.Status,
        p.Client?.DisplayName,
        p.Owner.Name,
        p.StartDate,
        p.TargetDate,
        ProjectOverdue.IsOverdue(p.TargetDate, p.Status, today),
        ProjectOverdue.DaysOverdue(p.TargetDate, p.Status, today),
        p.ProgressPct,
        p.Location,
        p.Work is { } w ? new ProjectWorkRow(ProtonText.Hours(w.TotalMinutes), ProtonText.Hours(w.MinutesLast14Days), w.ContributorCount, w.LastStartedAt) : null,
        p.Money is { } m ? new ProjectMoneyRow(m.Revenue, m.Cost, m.Profit, m.Margin, m.MarginKnown, m.Invoiced, m.Outstanding, m.HasUnconverted) : null);
}

public sealed record ProjectCard(
    long Id,
    string Title,
    string Domain,
    ProjectStatus Status,
    BillingType BillingType,
    string? Description,
    string? Location,
    ProtonNamed? Client,
    ProtonNamed Owner,
    DateOnly StartDate,
    DateOnly? TargetDate,
    bool Overdue,
    int? DaysOverdue,
    int ProgressPct,
    DateTime? CompletedAt,
    DateTime? HeldSince,
    DateTime CreatedAt,
    IReadOnlyList<string>? SiteRecorders,
    int? SiteRecorderCount);

public sealed record ProtonNamed(long Id, string Name);

/// <summary>One project, as GET /api/projects/{id} returns it. Money is left to get_project_financials.</summary>
public sealed class GetProjectTool(IProjectService projects) : ProtonTool<ProjectIdInput>
{
    public override string Name => "get_project";

    public override string Description =>
        "A project's details: status, client, owner, dates, progress, whether it is overdue. Use get_project_team for " +
        "who worked on it and get_project_financials for its money.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(ProjectIdInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var p = await projects.GetAsync(input.ProjectId, cancellationToken);
        var card = new ProjectCard(
            p.Id, p.Title, p.Domain, p.Status, p.BillingType,
            ProtonText.Cut(p.Description, 600), p.Location,
            p.Client is { } c ? new ProtonNamed(c.Id, c.DisplayName) : null,
            new ProtonNamed(p.Owner.Id, p.Owner.Name),
            p.StartDate, p.TargetDate,
            ProjectOverdue.IsOverdue(p.TargetDate, p.Status, context.Today),
            ProjectOverdue.DaysOverdue(p.TargetDate, p.Status, context.Today),
            p.ProgressPct, p.CompletedAt, p.HeldSince, p.CreatedAt,
            p.Team?.People.Select(x => x.Name).ToList(), p.Team?.Count);

        var refs = new List<ProtonRef>
        {
            new(ProtonRefType.Project, p.Id, p.Title, p.Domain),
            new(ProtonRefType.Person, p.Owner.Id, p.Owner.Name),
        };
        if (p.Client is { } client)
        {
            refs.Add(new ProtonRef(ProtonRefType.Client, client.Id, client.DisplayName, p.Domain));
        }

        return new ProtonToolOutput(card, refs);
    }
}

public sealed record RequirementsInput
{
    public required long ProjectId { get; init; }

    [Description("Only requirements not done yet.")]
    public bool OpenOnly { get; init; }
}

public sealed record RequirementRow(
    long Id, string Text, DateOnly? Deadline, bool Overdue, bool Done, DateTime? DoneAt, string? DoneBy, IReadOnlyList<string> Assignees);

public sealed record RequirementList(int Total, int Done, int Overdue, int ProgressPct, IReadOnlyList<RequirementRow> Requirements);

public sealed class GetProjectRequirementsTool(IRequirementService requirements) : ProtonTool<RequirementsInput>
{
    private const int MaxRows = 40;

    public override string Name => "get_project_requirements";

    public override string Description => "A project's requirements (its task list): what is done, open, overdue, and who is assigned.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<RequirementsController>(nameof(RequirementsController.List));

    protected override async Task<ProtonToolOutput> RunAsync(RequirementsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var list = await requirements.ListAsync(input.ProjectId, cancellationToken);
        var rows = list.Items
            .Where(r => !input.OpenOnly || !r.IsDone)
            .Take(MaxRows)
            .Select(r => new RequirementRow(
                r.Id, ProtonText.Cut(r.Text, 300)!, r.Deadline, r.IsOverdue, r.IsDone, r.DoneAt, r.DoneBy?.Name,
                r.Assignees.Select(a => a.Name).ToList()))
            .ToList();
        return ProtonToolOutput.Of(new RequirementList(list.Total, list.Done, list.Overdue, list.ProgressPct, rows));
    }
}

public sealed record ProjectTeamInput
{
    public required long ProjectId { get; init; }

    [Description("Limits the hours to a period. Leave out for all time.")]
    public ProtonPeriod? Period { get; init; }

    [Description("First day, for a custom period (yyyy-mm-dd).")]
    public DateOnly? From { get; init; }

    [Description("Last day, for a custom period (yyyy-mm-dd).")]
    public DateOnly? To { get; init; }
}

public sealed record TeamMember(long Id, string Name, decimal Hours, decimal OvertimeHours, int Entries, int SharePct, DateTime LastWorkAt);

public sealed record ProjectTeam(
    long ProjectId,
    string Title,
    string Domain,
    DateOnly? From,
    DateOnly? To,
    decimal? HoursInPeriod,
    decimal? HoursAllTime,
    int? PeopleAllTime,
    IReadOnlyList<TeamMember>? People,
    IReadOnlyList<string>? SiteRecorders,
    string Note);

/// <summary>
/// Who works on a project. Projects have no member list: on Design projects it is the people who logged hours
/// (GET /api/projects/{id}/work), on Execution sites the people who recorded materials or labour.
/// </summary>
public sealed class GetProjectTeamTool(IProjectService projects, IWorkInsightsService insights) : ProtonTool<ProjectTeamInput>
{
    private static readonly ProtonEndpoint ProjectWork = ProtonEndpoint.Of<WorkInsightsController>(nameof(WorkInsightsController.ProjectWork));

    public override string Name => "get_project_team";

    public override string Description =>
        "Who worked on a project and their hours, optionally within a period. Design projects only track hours; " +
        "for Execution sites it lists who recorded materials and labour.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(ProjectTeamInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var projectRef = new ProtonRef(ProtonRefType.Project, project.Id, project.Title, project.Domain);
        if (project.Domain != DomainCodes.Design)
        {
            var recorders = project.Team?.People.Select(p => p.Name).ToList() ?? [];
            return ProtonToolOutput.Of(
                new ProjectTeam(project.Id, project.Title, project.Domain, null, null, null, null, project.Team?.Count, null, recorders,
                    "Execution sites don't track hours. These people recorded materials or labour weeks, latest first."),
                projectRef);
        }

        await context.RequireAsync(ProjectWork);
        var range = input.Period is null ? null : context.Period(input.Period, input.From, input.To, ProtonPeriod.ThisMonth);
        var query = new ProjectWorkQuery { From = range?.From, To = range?.To };
        await context.ValidateAsync(query, cancellationToken);
        var work = await insights.ProjectWorkAsync(project.Id, query, cancellationToken);

        var people = work.Contributors
            .Select(c => new TeamMember(c.UserId, c.Name, ProtonText.Hours(c.Minutes), ProtonText.Hours(c.OvertimeMinutes), c.Entries, c.SharePct, c.LastStartedAt))
            .ToList();
        var team = new ProjectTeam(
            project.Id, project.Title, project.Domain, range?.From, range?.To,
            range is null ? null : ProtonText.Hours(work.Contributors.Sum(c => c.Minutes)),
            ProtonText.Hours(work.TotalMinutes), work.ContributorCount, people, null,
            "People who logged hours on the project; projects have no separate member list.");
        return new ProtonToolOutput(
            team,
            [projectRef, .. people.Select(p => new ProtonRef(ProtonRefType.Person, p.Id, p.Name))],
            range);
    }
}

public sealed record ProjectFinancials(
    long ProjectId,
    string Title,
    string Currency,
    decimal Revenue,
    bool RevenueLocked,
    decimal? ProjectFee,
    decimal? MaterialMarkup,
    decimal? MaterialCharge,
    decimal? UnsentMaterialCharge,
    IReadOnlyList<CurrencyTotalResponse> Expenses,
    ProjectBillingResponse Billing,
    ProjectCostResponse Cost,
    decimal Profit,
    decimal? Margin,
    decimal RecognisedToDate,
    ProjectProjectionResponse? Projected,
    decimal LabourHours,
    decimal? CashProfit,
    decimal? MaterialProfit,
    string Note);

/// <summary>
/// A project's money, as GET /api/projects/{id} includes it for people with financial access. The cost per hour is
/// left out: on a one-person project it would be that person's rate.
/// </summary>
public sealed class GetProjectFinancialsTool(IProjectService projects) : ProtonTool<ProjectIdInput>
{
    public override string Name => "get_project_financials";

    public override string Description =>
        "A project's money, all time, in USD: revenue, received, invoiced, outstanding and overdue, cost by source " +
        "(labour, expenses, materials, ground labour), profit, margin and projection.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(ProjectIdInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var p = await projects.GetAsync(input.ProjectId, cancellationToken);
        var m = p.Money ?? throw new ForbiddenException(ErrorCodes.Forbidden, "This user can't see financial figures.");
        return ProtonToolOutput.Of(
            new ProjectFinancials(
                p.Id, p.Title, m.Currency, m.Revenue, m.IsLocked, m.ProjectFee, m.MaterialMarkup, m.MaterialCharge,
                m.UnsentMaterialCharge, m.ExpenseTotals, m.Billing, m.Cost, m.Profit, m.Margin, m.RecognisedToDate,
                m.Projected, ProtonText.Hours(m.LabourHours), m.CashProfit, m.MaterialProfit,
                "All-time figures in USD. Margin is empty until it means something (project done, 5% progress or 10 hours logged). " +
                "Costs without an exchange rate for their day are listed as unconverted, not counted."),
            new ProtonRef(ProtonRefType.Project, p.Id, p.Title, p.Domain));
    }
}

public sealed record SiteSummary(
    long ProjectId, string Title, int MaterialCount, decimal MaterialCost, int ReportedLabourWeeks, decimal GroundLabourCost, string Currency);

public sealed class GetSiteSummaryTool(IProjectService projects, ISiteRecordsService siteRecords) : ProtonTool<ProjectIdInput>
{
    public override string Name => "get_site_summary";

    public override string Description => "An Execution site's recorded materials and ground labour, in USD.";

    public override string? OnlyInDomain => DomainCodes.Execution;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<SiteRecordsController>(nameof(SiteRecordsController.Summaries));

    protected override async Task<ProtonToolOutput> RunAsync(ProjectIdInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        if (project.Domain != DomainCodes.Execution)
        {
            throw new ProtonToolInputException("That is a Design project; site records exist for Execution sites only.");
        }

        var query = new SiteSummaryQuery { Ids = [project.Id] };
        await context.ValidateAsync(query, cancellationToken);
        var summary = (await siteRecords.SummariesAsync(query, cancellationToken)).FirstOrDefault()
            ?? throw new NotFoundException("Site not found.");
        return ProtonToolOutput.Of(
            new SiteSummary(project.Id, project.Title, summary.MaterialCount, summary.MaterialCost, summary.ReportedWeeks, summary.GroundLabour, "USD"),
            new ProtonRef(ProtonRefType.Project, project.Id, project.Title, project.Domain));
    }
}
