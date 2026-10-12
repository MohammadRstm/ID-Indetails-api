using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Meetings.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Meetings.Implementation;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Settings.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

/// <summary>
/// Totals are calculated on each request from the entries themselves (see <see cref="WorkStats"/>), so they are
/// never stale. Employees see every open project of their domain but only their own hours on it.
/// </summary>
public sealed class MyWorkService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IWorkEntryRepository entries,
    ICompanyRepository company,
    IDomainSettingsService domainSettings,
    IMeetingRepository meetings,
    IOvertimeService overtime,
    IClock clock) : IMyWorkService
{
    public const int SummaryWeeks = 16;

    /// <summary>Days of daily totals in a work summary, ending today: a year.</summary>
    public const int DailyDays = 365;

    public const int RecentOnProject = 12;
    public const int WeeksOnProject = 8;
    public const int TeamActivityOnProject = 6;

    public async Task<WorkSummaryResponse> SummaryForMeAsync(WorkSummaryQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var person = (await entries.PeopleAsync([actor.UserId], cancellationToken)).Single();
        return await SummaryAsync(person, query, cancellationToken);
    }

    public async Task<WorkSummaryResponse> SummaryForPersonAsync(long userId, WorkSummaryQuery query, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.WorkEntryViewAll);
        var person = (await entries.PeopleAsync([userId], cancellationToken)).SingleOrDefault();
        if (person is null || !EmployeeRules.CanSee(actor, person.DomainCode))
        {
            throw new NotFoundException("Employee not found.");
        }

        return await SummaryAsync(person, query, cancellationToken);
    }

    public async Task<IReadOnlyList<MyProjectResponse>> MyProjectsAsync(CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        if (!actor.CanSee(DomainCodes.Design))
        {
            return [];
        }

        var settings = await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken);
        var open = await entries.OpenProjectsAsync(cancellationToken);
        var contributions = (await entries.ContributionsAsync(open.Select(p => p.Id).ToList(), cancellationToken)).ToLookup(c => c.ProjectId);

        return open
            .Select(p =>
            {
                var people = contributions[p.Id].ToList();
                var mine = people.SingleOrDefault(c => c.UserId == actor.UserId);
                var last = people.MaxBy(c => c.LastStartedAt);
                return new MyProjectResponse(
                    p.Id,
                    p.Title,
                    p.Description,
                    p.Icon,
                    p.Tint,
                    p.ClientName,
                    p.Location,
                    p.Status,
                    p.ProgressPct,
                    p.StartDate,
                    p.CreatedAt,
                    WorkEntryRules.AcceptsWork(p.Status, settings.HoldLogging),
                    mine?.Minutes ?? 0,
                    mine?.LastStartedAt,
                    people.OrderByDescending(c => c.LastStartedAt).Select(c => new ContributorResponse(c.UserId, c.UserName)).ToList(),
                    last is null ? null : new LastActivityResponse(last.UserId, last.UserName, last.LastStartedAt));
            })
            // Projects the caller worked on first, most recent first; the rest newest first.
            .OrderByDescending(p => p.MyLastStartedAt ?? DateTime.MinValue)
            .ThenByDescending(p => p.CreatedAt)
            .ToList();
    }

    public async Task<MyProjectWorkResponse> MyProjectWorkAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        if (project.DomainCode != DomainCodes.Design)
        {
            throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Work is only logged on Design projects.");
        }

        var settings = await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken);
        var calendar = CompanyCalendar.Of(await company.GetSettingsAsync(cancellationToken: cancellationToken));
        var today = clock.CompanyToday;
        var all = await entries.ListAsync(new WorkEntrySearch(ProjectId: projectId), cancellationToken);
        var mine = all.Where(e => e.UserId == actor.UserId).ToList();
        var myWorked = mine.Select(ToWorked).ToList();
        var overtimeById = await overtime.OvertimeOfAsync(mine, cancellationToken);

        return new MyProjectWorkResponse(
            projectId,
            WorkEntryRules.AcceptsWork(project.Status, settings.HoldLogging),
            mine.Sum(e => e.Minutes),
            mine.Sum(e => overtimeById.GetValueOrDefault(e.Id)),
            mine.Count,
            WorkStats.Minutes(myWorked, WorkStats.WeekStartOf(today, calendar.FirstDayOfWeek), today),
            all.Select(e => e.UserId).Distinct().Count(),
            all.Sum(e => e.Minutes),
            mine.OrderByDescending(e => e.StartedAt).Take(RecentOnProject)
                .Select(e => WorkEntryMapper.ToResponse(e, actor.UserId, settings, clock, overtimeById)).ToList(),
            ToWeeks(WorkStats.Weekly(myWorked, today, WeeksOnProject, calendar.FirstDayOfWeek)),
            all.Where(e => e.UserId != actor.UserId).OrderByDescending(e => e.StartedAt).Take(TeamActivityOnProject)
                .Select(e => new TeamActivityResponse(e.UserId, e.UserName, e.StartedAt, e.Description)).ToList());
    }

    public async Task<MyDayResponse> MyDayAsync(DateOnly? date, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var day = date ?? clock.CompanyToday;
        var from = clock.CompanyDayStartUtc(day);
        var to = clock.CompanyDayStartUtc(day.AddDays(1));
        var settings = await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken);
        var mine = await entries.ListAsync(new WorkEntrySearch(actor.UserId, From: from, To: to), cancellationToken);
        var overtimeById = await overtime.OvertimeOfAsync(mine, cancellationToken);
        var domainMeetings = actor.CanSee(DomainCodes.Design)
            ? await meetings.ListAsync(new MeetingSearch([DomainCodes.Design], From: from, To: to), cancellationToken)
            : [];

        return new MyDayResponse(
            day,
            mine.Sum(e => e.Minutes),
            mine.Sum(e => overtimeById.GetValueOrDefault(e.Id)),
            mine.Select(e => WorkEntryMapper.ToResponse(e, actor.UserId, settings, clock, overtimeById)).ToList(),
            domainMeetings.Select(MeetingMapper.ToResponse).ToList());
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken) => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private async Task<WorkSummaryResponse> SummaryAsync(WorkPerson person, WorkSummaryQuery query, CancellationToken cancellationToken)
    {
        var views = await entries.ListAsync(new WorkEntrySearch(UserId: person.Id), cancellationToken);
        var overtimeById = await overtime.OvertimeOfAsync(views, cancellationToken);
        var calendar = CompanyCalendar.Of(await company.GetSettingsAsync(cancellationToken: cancellationToken));
        var today = clock.CompanyToday;
        var worked = views.Select(ToWorked).ToList();

        var weekStart = WorkStats.WeekStartOf(today, calendar.FirstDayOfWeek);
        var quarterStart = WorkStats.QuarterStartOf(today);
        var lastQuarterStart = quarterStart.AddMonths(-3);
        var elapsedDays = today.DayNumber - quarterStart.DayNumber;
        var yearAgo = today.AddDays(-(DailyDays - 1));
        var (activeDays, longestStreak) = WorkStats.Streaks(worked, yearAgo, today);
        var words = query.Search?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

        var inRange = views.Where(e =>
        {
            var day = clock.ToCompanyDate(e.StartedAt);
            return (query.From is null || day >= query.From)
                && (query.To is null || day <= query.To)
                && (query.ProjectId is null || e.ProjectId == query.ProjectId)
                && words.All(w => e.Description.Contains(w, StringComparison.OrdinalIgnoreCase)
                    || e.ProjectTitle.Contains(w, StringComparison.OrdinalIgnoreCase));
        });

        return new WorkSummaryResponse(
            new WorkEntryPersonResponse(person.Id, person.Name),
            today,
            calendar.FirstDayOfWeek,
            WorkStats.Minutes(worked, today, today),
            Enumerable.Range(0, 7)
                .Select(i => weekStart.AddDays(i))
                .Select(d => new DayTotalResponse(d, WorkStats.Minutes(worked, d, d)))
                .ToList(),
            WorkStats.Minutes(worked, weekStart, today),
            WorkStats.Minutes(worked, weekStart.AddDays(-7), weekStart.AddDays(-1)),
            WorkStats.Minutes(worked, WorkStats.MonthStartOf(today), today),
            WorkStats.Minutes(worked, quarterStart, today),
            WorkStats.Minutes(worked, lastQuarterStart, Min(lastQuarterStart.AddDays(elapsedDays), quarterStart.AddDays(-1))),
            views.Sum(e => e.Minutes),
            views.Sum(e => overtimeById.GetValueOrDefault(e.Id)),
            views.Count,
            worked.Count > 0 ? worked.Min(w => w.Day) : null,
            views.Select(e => e.ProjectId).Distinct().Count(),
            activeDays,
            longestStreak,
            ToWeeks(WorkStats.Weekly(worked, today, SummaryWeeks, calendar.FirstDayOfWeek)),
            WorkStats.Daily(worked.Where(w => w.Day >= yearAgo)).Select(d => new DayTotalResponse(d.Day, d.Minutes)).ToList(),
            inRange
                .GroupBy(e => e.ProjectId)
                .Select(g => new ProjectTimeResponse(
                    g.Key,
                    g.First().ProjectTitle,
                    g.First().ProjectIcon,
                    g.First().ProjectTint,
                    g.First().ProjectStatus,
                    g.Sum(e => e.Minutes),
                    g.Sum(e => overtimeById.GetValueOrDefault(e.Id)),
                    g.Count(),
                    g.Max(e => e.StartedAt)))
                .OrderByDescending(p => p.Minutes)
                .ToList());
    }

    private WorkStats.Worked ToWorked(WorkEntryView e) => new(e.Id, e.UserId, e.ProjectId, clock.ToCompanyTime(e.StartedAt), e.Minutes);

    private static IReadOnlyList<WeekTotalResponse> ToWeeks(IReadOnlyList<WorkStats.WeekTotal> weeks)
        => weeks.Select(w => new WeekTotalResponse(w.WeekStart, w.Minutes, w.Entries)).ToList();

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
