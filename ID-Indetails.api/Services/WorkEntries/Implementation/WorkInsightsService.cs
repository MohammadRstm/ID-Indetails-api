using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.Meetings.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Settings.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

/// <summary>
/// Calculated from the entries on each request (see <see cref="WorkStats"/>). Labour cost is only included for
/// callers with <c>financial:view</c>, and is priced by the shared finance calculator.
/// </summary>
public sealed class WorkInsightsService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IWorkEntryRepository entries,
    ICompanyRepository company,
    IProjectFinanceRepository financeSources,
    IProjectFinanceService projectFinance,
    IDomainSettingsService domainSettings,
    IMeetingRepository meetings,
    IActivityFeedRepository activityFeed,
    IClock clock) : IWorkInsightsService
{
    public const int MinProjectWeeks = 4;
    public const int MaxProjectWeeks = 30;
    public const int SparkWeeks = 8;
    public const int ContributorWeeks = 10;
    public const int ContributorEntries = 40;
    public const int FocusDays = 14;
    public const int MaxTimelineEvents = 1000;

    private static readonly IReadOnlyList<string> TimelineKinds =
    [
        ActivityEventKinds.ProjectCreated,
        ActivityEventKinds.ProjectHold,
        ActivityEventKinds.ProjectResumed,
        ActivityEventKinds.ProjectCompleted,
        ActivityEventKinds.ProjectArchived,
        ActivityEventKinds.ProjectReopened,
        ActivityEventKinds.ProgressSet,
    ];

    public async Task<ProjectWorkResponse> ProjectWorkAsync(long projectId, ProjectWorkQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await DesignProjectAsync(actor, projectId, cancellationToken);
        var calendar = await CalendarAsync(cancellationToken);
        var today = clock.CompanyToday;
        var all = await entries.ListAsync(new WorkEntrySearch(ProjectId: projectId), cancellationToken);
        var worked = all.Select(ToWorked).ToList();

        var end = project.CompletedAt is { } completedAt ? clock.ToCompanyDate(completedAt) : today;
        var weeks = Math.Clamp((int)Math.Ceiling((end.DayNumber - project.StartDate.DayNumber + 1) / 7.0), MinProjectWeeks, MaxProjectWeeks);

        var inRange = all.Where(e => InRange(e, query.From, query.To)).ToList();
        var rangeMinutes = inRange.Sum(e => e.Minutes);
        var pricer = actor.Has(PermissionCodes.FinancialView)
            ? await LabourPricer.LoadAsync(financeSources, projectFinance, inRange.Select(ToRow).ToList(), clock, cancellationToken)
            : null;

        var contributors = inRange
            .GroupBy(e => e.UserId)
            .Select(g =>
            {
                var minutes = g.Sum(e => e.Minutes);
                return new ProjectContributorResponse(
                    g.Key,
                    g.First().UserName,
                    g.Count(),
                    minutes,
                    SharePct(minutes, rangeMinutes),
                    g.Max(e => e.StartedAt),
                    WorkStats.Weekly(worked.Where(w => w.UserId == g.Key), today, SparkWeeks, calendar.FirstDayOfWeek).Select(w => w.Minutes).ToList(),
                    pricer?.Price(g.Select(ToRow)));
            })
            .OrderByDescending(c => c.Minutes)
            .ToList();

        var last14 = WorkStats.Minutes(worked, today.AddDays(-13), today);
        return new ProjectWorkResponse(
            projectId,
            all.Sum(e => e.Minutes),
            all.Count,
            all.Select(e => e.UserId).Distinct().Count(),
            all.Count > 0 ? all.Max(e => e.StartedAt) : null,
            WorkStats.LevelOf(last14),
            last14,
            WorkStats.Minutes(worked, today.AddDays(-27), today.AddDays(-14)),
            ToWeeks(WorkStats.Weekly(worked, end, weeks, calendar.FirstDayOfWeek)),
            WorkStats.HourHeat(worked).Select(c => new HourCellResponse(c.Weekday, c.Hour, c.Minutes)).ToList(),
            contributors);
    }

    public async Task<ProjectContributorDetailResponse> ContributorAsync(long projectId, long userId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await DesignProjectAsync(actor, projectId, cancellationToken);
        var calendar = await CalendarAsync(cancellationToken);
        var settings = await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken);
        var all = await entries.ListAsync(new WorkEntrySearch(ProjectId: projectId), cancellationToken);
        var theirs = all.Where(e => e.UserId == userId).ToList();
        if (theirs.Count == 0)
        {
            throw new NotFoundException("This person has no work on the project.");
        }

        var minutes = theirs.Sum(e => e.Minutes);
        return new ProjectContributorDetailResponse(
            projectId,
            new WorkEntryPersonResponse(userId, theirs[0].UserName),
            minutes,
            theirs.Count,
            SharePct(minutes, all.Sum(e => e.Minutes)),
            ToWeeks(WorkStats.Weekly(theirs.Select(ToWorked), clock.CompanyToday, ContributorWeeks, calendar.FirstDayOfWeek)),
            theirs.OrderByDescending(e => e.StartedAt).Take(ContributorEntries)
                .Select(e => WorkEntryMapper.ToResponse(e, actor.UserId, settings, clock)).ToList());
    }

    public async Task<ProjectTimelineResponse> TimelineAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await DesignProjectAsync(actor, projectId, cancellationToken);
        var end = project.CompletedAt is { } completedAt ? clock.ToCompanyDate(completedAt) : clock.CompanyToday;

        var parts = (await entries.ListAsync(new WorkEntrySearch(ProjectId: projectId), cancellationToken))
            .SelectMany(e => WorkStats.SplitByDay(clock.ToCompanyTime(e.StartedAt), e.Minutes).Select(d => (d.Day, d.Minutes, e.UserId, e.UserName)))
            .ToList();
        var projectMeetings = (await meetings.ListAsync(new MeetingSearch([DomainCodes.Design], projectId), cancellationToken))
            .ToLookup(m => clock.ToCompanyDate(m.StartsAt));
        var byDay = parts.ToLookup(p => p.Day);

        var days = byDay.Select(g => g.Key)
            .Union(projectMeetings.Select(g => g.Key))
            .Order()
            .Select(day => new TimelineDayResponse(
                day,
                byDay[day].Sum(p => p.Minutes),
                byDay[day]
                    .GroupBy(p => p.UserId)
                    .Select(g => new TimelinePersonResponse(g.Key, g.First().UserName, g.Sum(p => p.Minutes)))
                    .OrderByDescending(p => p.Minutes)
                    .ToList(),
                projectMeetings[day].Select(m => new TimelineMeetingResponse(m.Id, m.Topic, m.StartsAt, m.EndsAt)).ToList()))
            .ToList();

        var (events, _) = await activityFeed.SearchAsync(
            new ActivityFeedSearch([DomainCodes.Design], false, TimelineKinds, [], false, projectId, null, null, 1, MaxTimelineEvents),
            cancellationToken);

        return new ProjectTimelineResponse(
            projectId,
            project.StartDate,
            end,
            days,
            events.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
                .Select(e => new TimelineEventResponse(e.Kind, e.OccurredAt, e.ActorName, e.Kind == ActivityEventKinds.ProgressSet ? e.Amount : null))
                .ToList(),
            WorkStats.Gaps(byDay.Select(g => g.Key).ToHashSet(), project.StartDate, end)
                .Select(g => new TimelineGapResponse(g.From, g.To, g.Days))
                .ToList());
    }

    public async Task<IReadOnlyList<PersonWorkResponse>> PeopleAsync(PeopleWorkQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        if (!actor.CanSee(DomainCodes.Design))
        {
            return [];
        }

        var words = query.Search?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var people = (await entries.DesignPeopleAsync(cancellationToken))
            .Where(p => query.TeamId is null || p.TeamId == query.TeamId)
            .Where(p => words.All(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                || p.Email.Contains(w, StringComparison.OrdinalIgnoreCase)
                || (p.Position?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false)))
            .ToList();

        var rows = (await entries.RowsAsync(new WorkEntrySearch(), cancellationToken)).ToLookup(r => r.UserId);
        var calendar = await CalendarAsync(cancellationToken);
        var today = clock.CompanyToday;
        var weekStart = WorkStats.WeekStartOf(today, calendar.FirstDayOfWeek);
        var quarterStart = WorkStats.QuarterStartOf(today);
        var focusFrom = today.AddDays(-(FocusDays - 1));
        var pricer = actor.Has(PermissionCodes.FinancialView)
            ? await LabourPricer.LoadAsync(financeSources, projectFinance, people.SelectMany(p => rows[p.Id]).ToList(), clock, cancellationToken)
            : null;

        var worked = people.ToDictionary(p => p.Id, p => rows[p.Id].Select(ToWorked).ToList());
        var focusOf = worked.ToDictionary(
            w => w.Key,
            w => w.Value.Where(x => x.Day >= focusFrom).GroupBy(x => x.ProjectId).MaxBy(g => g.Sum(x => x.Minutes))?.Key);
        var titles = await entries.ProjectTitlesAsync(focusOf.Values.OfType<long>().Distinct().ToList(), cancellationToken);

        return people
            .Select(p =>
            {
                var mine = worked[p.Id];
                var focus = focusOf[p.Id];
                return new PersonWorkResponse(
                    p.Id,
                    p.Name,
                    p.Email,
                    p.Position,
                    p.TeamName,
                    p.Role,
                    p.Status,
                    WorkStats.Minutes(mine, weekStart, today),
                    WorkStats.Minutes(mine, quarterStart, today),
                    mine.Sum(x => x.Minutes),
                    mine.Count,
                    rows[p.Id].Select(r => (DateTime?)r.StartedAt).Max(),
                    focus is { } id ? new FocusProjectResponse(id, titles.GetValueOrDefault(id, "")) : null,
                    WorkStats.Weekly(mine, today, SparkWeeks, calendar.FirstDayOfWeek).Select(w => w.Minutes).ToList(),
                    pricer?.Price(rows[p.Id]));
            })
            .OrderByDescending(p => p.WeekMinutes)
            .ThenBy(p => p.Name)
            .ToList();
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken) => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private async Task<CompanyCalendar> CalendarAsync(CancellationToken cancellationToken)
        => CompanyCalendar.Of(await company.GetSettingsAsync(cancellationToken: cancellationToken));

    private async Task<Project> DesignProjectAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        return project.DomainCode == DomainCodes.Design
            ? project
            : throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Work is only logged on Design projects.");
    }

    private bool InRange(WorkEntryView e, DateOnly? from, DateOnly? to)
    {
        var day = clock.ToCompanyDate(e.StartedAt);
        return (from is null || day >= from) && (to is null || day <= to);
    }

    private WorkStats.Worked ToWorked(WorkEntryView e) => new(e.Id, e.UserId, e.ProjectId, clock.ToCompanyTime(e.StartedAt), e.Minutes);

    private WorkStats.Worked ToWorked(WorkRow r) => new(r.Id, r.UserId, r.ProjectId, clock.ToCompanyTime(r.StartedAt), r.Minutes);

    private static WorkRow ToRow(WorkEntryView e) => new(e.Id, e.UserId, e.ProjectId, e.StartedAt, e.EndedAt, e.Minutes, e.CreatedAt);

    private static int SharePct(int minutes, int total) => total > 0 ? (int)Math.Round(minutes * 100m / total, MidpointRounding.AwayFromZero) : 0;

    private static IReadOnlyList<WeekTotalResponse> ToWeeks(IReadOnlyList<WorkStats.WeekTotal> weeks)
        => weeks.Select(w => new WeekTotalResponse(w.WeekStart, w.Minutes, w.Entries)).ToList();
}
