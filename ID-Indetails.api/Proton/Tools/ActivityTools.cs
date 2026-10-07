using System.ComponentModel;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Activity;
using ID_Indetails.api.DTO.Request.Meetings;
using ID_Indetails.api.DTO.Response.Dashboard;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Activity.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Dashboard.Abstraction;
using ID_Indetails.api.Services.Meetings.Abstraction;

namespace ID_Indetails.api.Proton.Tools;

public sealed record RecentActivityInput
{
    public ProtonDomain? Domain { get; init; }

    public long? ProjectId { get; init; }

    public long? ClientId { get; init; }

    [Description("The person who did it.")]
    public long? UserId { get; init; }

    [Description("work, money, billing, projects, people or company.")]
    public ActivityGroup? Group { get; init; }

    [Description("Default last_7_days.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    [Description("Events to return, 1 to 30 (default 20), newest first.")]
    public int? Limit { get; init; }
}

public sealed record ActivityRow(
    string When,
    string Kind,
    ActivityGroup Group,
    string? Domain,
    string? Actor,
    ProtonNamed? Project,
    ProtonNamed? Client,
    decimal? Amount,
    decimal? PreviousAmount,
    int? WorkMinutes,
    string? WorkDescription);

public sealed record ActivityList(DateOnly From, DateOnly To, bool MoreThanShown, IReadOnlyList<ActivityRow> Events);

/// <summary>
/// The activity feed, as GET /api/activity returns it (it already hides what the user may not see). The feed has no
/// date filter, so pages are read newest first until the period's start, three pages at most.
/// </summary>
public sealed class GetRecentActivityTool(IActivityFeedService activity, IClock clock) : ProtonTool<RecentActivityInput>
{
    private const int PageSize = 40;
    private const int MaxPages = 3;

    public override string Name => "get_recent_activity";

    public override string Description =>
        "What happened: the activity feed (projects created or changed, requirements, meetings, work logged, " +
        "invoices, payments, people changes), newest first, for a period, domain, project, client or person.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<ActivityController>(nameof(ActivityController.List));

    protected override async Task<ProtonToolOutput> RunAsync(RecentActivityInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Group == ActivityGroup.Other)
        {
            throw new ProtonToolInputException("Pick another group or leave it out.");
        }

        var range = context.Period(input.Period, input.From, input.To, ProtonPeriod.Last7Days);
        var limit = ProtonText.Limit(input.Limit, 20, 30);
        var start = clock.CompanyDayStartUtc(range.From);
        var end = clock.CompanyDayStartUtc(range.To.AddDays(1));
        var query = new ActivityListQuery
        {
            Group = input.Group,
            Domain = input.Domain.Code(),
            ProjectId = input.ProjectId,
            ClientId = input.ClientId,
            UserId = input.UserId,
            PageSize = PageSize,
        };
        await context.ValidateAsync(query, cancellationToken);

        var events = new List<DTO.Response.Activity.ActivityItemResponse>();
        var reachedStart = false;
        for (var page = 1; page <= MaxPages && !reachedStart && events.Count < limit; page++)
        {
            var result = await activity.ListAsync(query with { Page = page }, cancellationToken);
            events.AddRange(result.Items.Where(e => e.OccurredAt >= start && e.OccurredAt < end));
            reachedStart = result.Items.Count < PageSize || result.Items.Any(e => e.OccurredAt < start);
        }

        var rows = events.Take(limit).Select(e => new ActivityRow(
            clock.Local(e.OccurredAt),
            e.Kind,
            e.Group,
            e.Domain,
            e.Actor?.Name,
            e.Project is { } p ? new ProtonNamed(p.Id, p.Name) : null,
            e.Client is { } c ? new ProtonNamed(c.Id, c.Name) : null,
            e.Amount,
            e.PrevAmount,
            e.WorkEntry?.Minutes,
            ProtonText.Cut(e.WorkEntry?.Description, 200))).ToList();
        var refs = events.Take(limit)
            .SelectMany(e => new[]
            {
                e.Project is { } p ? new ProtonRef(ProtonRefType.Project, p.Id, p.Name, e.Domain) : null,
                e.Client is { } c ? new ProtonRef(ProtonRefType.Client, c.Id, c.Name, e.Domain) : null,
                e.Actor is { } a ? new ProtonRef(ProtonRefType.Person, a.Id, a.Name) : null,
            })
            .OfType<ProtonRef>()
            .Distinct()
            .ToList();
        return new ProtonToolOutput(new ActivityList(range.From, range.To, events.Count > limit || !reachedStart, rows), refs, range);
    }
}

public sealed record MeetingsInput
{
    [Description("Default this_week. At most 92 days.")]
    public ProtonPeriod? Period { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public long? ProjectId { get; init; }

    public ProtonDomain? Domain { get; init; }
}

public sealed record MeetingRow(long Id, ProtonNamed Project, string Starts, string Ends, int Minutes, string? Topic, string? Description, string? SetBy);

public sealed record MeetingList(DateOnly From, DateOnly To, int Total, IReadOnlyList<MeetingRow> Meetings);

public sealed class ListMeetingsTool(IMeetingService meetings, IClock clock) : ProtonTool<MeetingsInput>
{
    private const int MaxRows = 50;

    public override string Name => "list_meetings";

    public override string Description => "Project meetings in a period (past or upcoming), for a project or domain.";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<MeetingsController>(nameof(MeetingsController.List));

    protected override async Task<ProtonToolOutput> RunAsync(MeetingsInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var range = context.Period(input.Period, input.From, input.To, ProtonPeriod.ThisWeek, maxDays: 92);
        var query = new MeetingListQuery { From = range.From, To = range.To, ProjectId = input.ProjectId, Domain = input.Domain.Code() };
        await context.ValidateAsync(query, cancellationToken);
        var list = await meetings.ListAsync(query, cancellationToken);

        var rows = list.Take(MaxRows).Select(m => new MeetingRow(
            m.Id,
            new ProtonNamed(m.Project.Id, m.Project.Title),
            clock.Local(m.StartsAt),
            clock.Local(m.EndsAt),
            m.Minutes,
            ProtonText.Cut(m.Topic, 200),
            ProtonText.Cut(m.Description, 300),
            m.CreatedBy?.Name)).ToList();
        return new ProtonToolOutput(
            new MeetingList(range.From, range.To, list.Count, rows),
            rows.Select(r => new ProtonRef(ProtonRefType.Project, r.Project.Id, r.Project.Name)).Distinct().ToList(),
            range);
    }
}

public sealed record DomainInput
{
    public required ProtonDomain Domain { get; init; }
}

public sealed record DomainDashboard(DashboardResponse Projects, UpcomingDeadlinesResponse Deadlines, string Note);

/// <summary>A domain's dashboard cards and deadlines, as GET /api/dashboard/{domain} and its deadlines return them.</summary>
public sealed class GetDomainDashboardTool(IDashboardService dashboard) : ProtonTool<DomainInput>
{
    public override string Name => "get_domain_dashboard";

    public override string Description =>
        "A domain at a glance: active projects, projects by status, client count, longest-running projects, and " +
        "requirement deadlines (overdue and due in the next 14 days).";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.Of<DashboardController>(nameof(DashboardController.Get));

    protected override async Task<ProtonToolOutput> RunAsync(DomainInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var domain = input.Domain.Code();
        var cards = await dashboard.GetAsync(domain, cancellationToken);
        var deadlines = await dashboard.DeadlinesAsync(domain, cancellationToken);
        return new ProtonToolOutput(
            new DomainDashboard(cards, deadlines, "Deadlines lists at most 8 requirements; total and overdue count them all."),
            deadlines.Items.Select(d => new ProtonRef(ProtonRefType.Project, d.Project.Id, d.Project.Title, domain)).Distinct().ToList());
    }
}
