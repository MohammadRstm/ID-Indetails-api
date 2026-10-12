using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Reports;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Reports;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.Reports.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Reports.Abstraction;
using ID_Indetails.api.Services.Settings.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Implementation;

namespace ID_Indetails.api.Services.Reports.Implementation;

/// <summary>
/// The Design reports and dashboard, calculated from the entries on each request (see <see cref="WorkStats"/>).
/// Money comes from the shared finance calculator and is only included for callers with <c>financial:view</c>.
/// </summary>
public sealed class ReportService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IWorkEntryRepository entries,
    IReportRepository reports,
    ICompanyRepository company,
    IProjectFinanceRepository financeSources,
    IProjectFinanceService projectFinance,
    IDomainSettingsService domainSettings,
    IOvertimeService overtime,
    IClock clock) : IReportService
{
    public const int MinReportWeeks = 3;
    public const int MaxReportWeeks = 20;
    public const int MatrixProjects = 6;
    public const int QuarterCount = 6;
    public const int IdleDays = 7;
    public const int CloseoutProgress = 90;
    public const int MaxAttentionItems = 5;
    public const int OverviewProjects = 8;

    public async Task<WorkReportResponse> WorkReportAsync(WorkReportQuery query, CancellationToken cancellationToken = default)
    {
        var (actor, from, to, views) = await WorkOfAsync(query, cancellationToken);
        var calendar = CompanyCalendar.Of(await company.GetSettingsAsync(cancellationToken: cancellationToken));
        var today = clock.CompanyToday;
        var worked = views.Select(ToWorked).ToList();
        var total = views.Sum(e => e.Minutes);
        var overtimeById = await overtime.OvertimeOfAsync(views, cancellationToken);
        var weeks = Math.Clamp((int)Math.Ceiling((to.DayNumber - from.DayNumber + 1) / 7.0), MinReportWeeks, MaxReportWeeks);
        // The bars are whole weeks, and the first ones can start before the range: their work is loaded too, so a bar
        // is never zero just because its days weren't read. Totals and breakdowns still cover the range only.
        var firstWeek = WorkStats.WeekStartOf(to, calendar.FirstDayOfWeek).AddDays(-7 * (weeks - 1));
        var before = firstWeek < from ? await EntriesAsync(query, firstWeek, from.AddDays(-1), cancellationToken) : [];
        var weekWorked = worked.Concat(before.Select(ToWorked)).ToList();
        var weekly = WorkStats.Weekly(weekWorked, to, weeks, calendar.FirstDayOfWeek);
        var weekWorkedByProject = weekWorked.GroupBy(w => w.ProjectId).ToDictionary(g => g.Key, g => g.ToList());
        var projects = (await reports.DesignProjectsAsync(cancellationToken)).ToDictionary(p => p.Id);

        var byProject = views
            .GroupBy(e => e.ProjectId)
            .Select(g =>
            {
                var minutes = g.Sum(e => e.Minutes);
                return new ReportProjectResponse(
                    g.Key,
                    g.First().ProjectTitle,
                    g.First().ProjectStatus,
                    minutes,
                    g.Count(),
                    g.Select(e => e.UserId).Distinct().Count(),
                    SharePct(minutes, total),
                    WorkStats.Weekly(weekWorkedByProject[g.Key], to, weeks, calendar.FirstDayOfWeek).Select(w => w.Minutes).ToList());
            })
            .OrderByDescending(p => p.Minutes)
            .ToList();

        var byPerson = views
            .GroupBy(e => e.UserId)
            .Select(g => new ReportPersonResponse(
                g.Key,
                g.First().UserName,
                g.Sum(e => e.Minutes),
                g.Sum(e => overtimeById.GetValueOrDefault(e.Id)),
                g.Count()))
            .OrderByDescending(p => p.Minutes)
            .ToList();

        var matrixProjects = byProject.Take(MatrixProjects).Select(p => new ReportMatrixProjectResponse(p.ProjectId, p.Title)).ToList();
        var cells = views.GroupBy(e => (e.UserId, e.ProjectId)).ToDictionary(g => g.Key, g => g.Sum(e => e.Minutes));
        var matrix = new ReportMatrixResponse(
            matrixProjects,
            byPerson
                .Select(p => new ReportMatrixRowResponse(
                    p.UserId,
                    p.Name,
                    p.Minutes,
                    p.OvertimeMinutes,
                    matrixProjects.Select(mp => cells.GetValueOrDefault((p.UserId, mp.ProjectId))).ToList()))
                .ToList());

        return new WorkReportResponse(
            from,
            to,
            total,
            views.Sum(e => overtimeById.GetValueOrDefault(e.Id)),
            views.Count,
            byProject.Count,
            byPerson.Count,
            views.Count > 0 ? total / views.Count : 0,
            weekly.Where(w => w.Minutes > 0).MaxBy(w => w.Minutes) is { } busiest ? new WeekTotalResponse(busiest.WeekStart, busiest.Minutes, busiest.Entries) : null,
            ToWeeks(weekly),
            byProject,
            ByClient(views),
            byPerson,
            matrix,
            await CompletedAsync(projects.Values, from, to, cancellationToken),
            await QuarterlyAsync(today, cancellationToken),
            actor.Has(PermissionCodes.FinancialView) ? await MoneyAsync(views, projects, byProject, from, to, cancellationToken) : null);
    }

    public async Task<WorkTotalsResponse> WorkTotalsAsync(WorkReportQuery query, CancellationToken cancellationToken = default)
    {
        var (_, from, to, views) = await WorkOfAsync(query, cancellationToken);
        return new WorkTotalsResponse(
            from,
            to,
            views.Sum(e => e.Minutes),
            views.Count,
            views
                .GroupBy(e => e.ProjectId)
                .Select(g => new ProjectTotalResponse(
                    g.Key, g.First().ProjectTitle, g.First().ProjectStatus, g.Sum(e => e.Minutes), g.Count(), g.Select(e => e.UserId).Distinct().Count()))
                .OrderByDescending(p => p.Minutes)
                .ToList(),
            views
                .GroupBy(e => e.UserId)
                .Select(g => new PersonTotalResponse(
                    g.Key, g.First().UserName, g.Sum(e => e.Minutes), g.Count(), g.Select(e => e.ProjectId).Distinct().Count()))
                .OrderByDescending(p => p.Minutes)
                .ToList(),
            ByClient(views));
    }

    /// <summary>The Design work a report covers, once the caller may see reports and Design.</summary>
    private async Task<(UserAccess Actor, DateOnly From, DateOnly To, IReadOnlyList<WorkEntryView> Views)> WorkOfAsync(
        WorkReportQuery query,
        CancellationToken cancellationToken)
    {
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.ReportView);
        var actor = await DesignViewerAsync(cancellationToken);
        var today = clock.CompanyToday;
        var from = query.From ?? WorkStats.QuarterStartOf(today);
        var to = query.To ?? today;
        return (actor, from, to, await EntriesAsync(query, from, to, cancellationToken));
    }

    /// <summary>The report's work between two company days, inclusive, with its filters.</summary>
    private Task<IReadOnlyList<WorkEntryView>> EntriesAsync(WorkReportQuery query, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        => entries.ListAsync(
            new WorkEntrySearch(
                query.UserId,
                query.ProjectId,
                query.ClientId,
                clock.CompanyDayStartUtc(from),
                clock.CompanyDayStartUtc(to.AddDays(1)),
                InternalOnly: query.Internal),
            cancellationToken);

    private static IReadOnlyList<ReportClientResponse> ByClient(IEnumerable<WorkEntryView> views)
        => views
            .GroupBy(e => e.ClientId)
            .Select(g => new ReportClientResponse(g.Key, g.First().ClientName, g.Sum(e => e.Minutes)))
            .OrderByDescending(c => c.Minutes)
            .ToList();

    public async Task<OverviewResponse> OverviewAsync(OverviewQuery query, CancellationToken cancellationToken = default)
    {
        (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.ReportView);
        await DesignViewerAsync(cancellationToken);
        var calendar = CompanyCalendar.Of(await company.GetSettingsAsync(cancellationToken: cancellationToken));
        var settings = await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken);
        var today = clock.CompanyToday;
        // a custom period may end before today, never after it
        var end = query.Period == OverviewPeriod.Custom ? Min(query.To!.Value, today) : today;
        var (from, previousFrom, previousTo) = query.Period == OverviewPeriod.Custom
            ? (query.From!.Value, query.From.Value.AddDays(-(end.DayNumber - query.From.Value.DayNumber + 1)), query.From.Value.AddDays(-1))
            : Periods(query.Period, today);
        var weekStart = WorkStats.WeekStartOf(today, calendar.FirstDayOfWeek);
        var weeks = (int)Math.Ceiling((end.DayNumber - from.DayNumber + 1) / 7.0);
        // the bars are whole weeks, the earliest starting before the previous period: read their work too
        var previousFirstWeek = WorkStats.WeekStartOf(previousTo, calendar.FirstDayOfWeek).AddDays(-7 * (weeks - 1));
        var since = Min(Min(previousFrom, weekStart), previousFirstWeek);

        var rows = await entries.RowsAsync(new WorkEntrySearch(From: clock.CompanyDayStartUtc(since)), cancellationToken);
        var worked = rows.Select(r => new WorkStats.Worked(r.Id, r.UserId, r.ProjectId, clock.ToCompanyTime(r.StartedAt), r.Minutes)).ToList();
        var current = worked.Where(w => w.Day >= from && w.Day <= end).ToList();
        var previous = worked.Where(w => w.Day >= previousFrom && w.Day <= previousTo).ToList();
        var thisWeek = worked.Where(w => w.Day >= weekStart && w.Day <= today).ToList();
        var overtimeById = await overtime.OvertimeOfAsync(rows.Where(r => clock.ToCompanyDate(r.StartedAt) >= from), cancellationToken);

        var projects = await reports.DesignProjectsAsync(cancellationToken);
        var titles = projects.ToDictionary(p => p.Id, p => p.Title);
        var people = (await entries.DesignPeopleAsync(cancellationToken)).Where(p => p.Status == UserStatus.Active).ToList();
        var logging = thisWeek.Select(w => w.UserId).ToHashSet();

        return new OverviewResponse(
            query.Period,
            from,
            end,
            previousFrom,
            previousTo,
            current.Sum(w => w.Minutes),
            current.Sum(w => overtimeById.GetValueOrDefault(w.Id)),
            previous.Sum(w => w.Minutes),
            current.Count,
            previous.Count,
            current.Count > 0 ? current.Sum(w => w.Minutes) / current.Count : 0,
            people.Count(p => logging.Contains(p.Id)),
            people.Count,
            ToWeeks(WorkStats.Weekly(worked, end, weeks, calendar.FirstDayOfWeek)),
            ToWeeks(WorkStats.Weekly(worked, previousTo, weeks, calendar.FirstDayOfWeek)),
            await AttentionAsync(projects, people, logging, settings, today, cancellationToken),
            ProjectMinutes(current, titles).Take(OverviewProjects).ToList(),
            projects.GroupBy(p => p.Status).Select(g => new StatusCountResponse(g.Key, g.Count())).OrderBy(s => s.Status).ToList(),
            ProjectMinutes(thisWeek, titles).FirstOrDefault(),
            current.Select(w => w.UserId).Distinct().Count(),
            current.Select(w => w.ProjectId).Distinct().Count(),
            thisWeek.Sum(w => w.Minutes));
    }

    /// <summary>The caller's access, once they are known to see Design; otherwise the Design reports are not found.</summary>
    private async Task<UserAccess> DesignViewerAsync(CancellationToken cancellationToken)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        return actor.CanSee(DomainCodes.Design) ? actor : throw new NotFoundException("Domain not found.");
    }

    /// <summary>
    /// The period so far and the one before it: quarter to date against the same number of days of the last quarter,
    /// month to date against the whole last month, the last 30 days against the 30 before.
    /// </summary>
    private static (DateOnly From, DateOnly PreviousFrom, DateOnly PreviousTo) Periods(OverviewPeriod period, DateOnly today)
    {
        switch (period)
        {
            case OverviewPeriod.Month:
                var month = WorkStats.MonthStartOf(today);
                return (month, month.AddMonths(-1), month.AddDays(-1));
            case OverviewPeriod.Last30:
                var start = today.AddDays(-29);
                return (start, start.AddDays(-30), start.AddDays(-1));
            default:
                var quarter = WorkStats.QuarterStartOf(today);
                var previous = quarter.AddMonths(-3);
                return (quarter, previous, Min(previous.AddDays(today.DayNumber - quarter.DayNumber), quarter.AddDays(-1)));
        }
    }

    /// <summary>Idle projects (only while the domain wants them flagged), projects near completion, projects on hold, and people not logging.</summary>
    private async Task<IReadOnlyList<AttentionItemResponse>> AttentionAsync(
        IReadOnlyList<Project> projects,
        IReadOnlyList<DesignPerson> people,
        IReadOnlySet<long> loggingThisWeek,
        DomainSettings settings,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var items = new List<AttentionItemResponse>();
        var active = projects.Where(p => p.Status == ProjectStatus.Active).ToList();
        if (settings.NotifyIdle)
        {
            var lastWork = (await entries.ContributionsAsync(active.Select(p => p.Id).ToList(), cancellationToken))
                .GroupBy(c => c.ProjectId)
                .ToDictionary(g => g.Key, g => clock.ToCompanyDate(g.Max(c => c.LastStartedAt)));
            items.AddRange(active
                .Select(p => (Project: p, Days: today.DayNumber - (lastWork.TryGetValue(p.Id, out var day) ? day : p.StartDate).DayNumber))
                .Where(x => x.Days >= IdleDays)
                .OrderByDescending(x => x.Days)
                .Select(x => new AttentionItemResponse(AttentionKind.Idle, x.Project.Id, x.Project.Title, x.Days, null, null)));
        }

        items.AddRange(active
            .Where(p => p.ProgressPct >= CloseoutProgress)
            .OrderByDescending(p => p.ProgressPct)
            .Select(p => new AttentionItemResponse(AttentionKind.Closeout, p.Id, p.Title, null, p.ProgressPct, null)));

        var held = projects.Where(p => p.Status == ProjectStatus.Hold).ToList();
        var heldSince = await reports.HeldSinceAsync(held.Select(p => p.Id).ToList(), cancellationToken);
        items.AddRange(held
            .Select(p => new AttentionItemResponse(
                AttentionKind.OnHold,
                p.Id,
                p.Title,
                heldSince.TryGetValue(p.Id, out var at) ? today.DayNumber - clock.ToCompanyDate(at).DayNumber : null,
                null,
                null)));

        var notLogging = people.Where(p => p.Role == RoleCodes.Employee && !loggingThisWeek.Contains(p.Id)).OrderBy(p => p.Name).ToList();
        if (notLogging.Count > 0)
        {
            items.Add(new AttentionItemResponse(
                AttentionKind.NotLogging, null, null, null, null, notLogging.Count,
                notLogging.Take(3).Select(p => new PersonRefResponse(p.Id, p.Name)).ToList()));
        }

        return items.Take(MaxAttentionItems).ToList();
    }

    private async Task<IReadOnlyList<CompletedProjectResponse>> CompletedAsync(
        IEnumerable<Project> projects,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var completed = projects
            .Where(p => p.CompletedAt is { } at && clock.ToCompanyDate(at) >= from && clock.ToCompanyDate(at) <= to)
            .ToList();
        var work = (await entries.ContributionsAsync(completed.Select(p => p.Id).ToList(), cancellationToken)).ToLookup(c => c.ProjectId);

        return completed
            .Select(p =>
            {
                var completedOn = clock.ToCompanyDate(p.CompletedAt!.Value);
                return new CompletedProjectResponse(
                    p.Id,
                    p.Title,
                    p.StartDate,
                    completedOn,
                    completedOn.DayNumber - p.StartDate.DayNumber,
                    work[p.Id].Sum(c => c.Minutes),
                    work[p.Id].Count());
            })
            .OrderByDescending(p => p.CompletedOn)
            .ToList();
    }

    private async Task<IReadOnlyList<QuarterTotalResponse>> QuarterlyAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var first = WorkStats.QuarterStartOf(today).AddMonths(-3 * (QuarterCount - 1));
        var rows = await entries.RowsAsync(new WorkEntrySearch(From: clock.CompanyDayStartUtc(first)), cancellationToken);
        var byQuarter = rows
            .GroupBy(r => WorkStats.QuarterStartOf(clock.ToCompanyDate(r.StartedAt)))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Minutes));

        return Enumerable.Range(0, QuarterCount)
            .Select(i => first.AddMonths(3 * i))
            .Select(q => new QuarterTotalResponse(q, byQuarter.GetValueOrDefault(q)))
            .ToList();
    }

    private async Task<ReportMoneyResponse> MoneyAsync(
        IReadOnlyList<WorkEntryView> views,
        IReadOnlyDictionary<long, Project> projects,
        IReadOnlyList<ReportProjectResponse> byProject,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var pricer = await LabourPricer.LoadAsync(financeSources, projectFinance, views.Select(ToRow).ToList(), clock, cancellationToken);
        var labour = pricer.Price(views.Select(ToRow));

        var rates = new ExchangeRateTable(await financeSources.ExchangeRatesAsync(cancellationToken));
        var expenses = ProjectFinance.ToUsd(
            (await financeSources.ExpensesAsync(projects.Keys.ToList(), cancellationToken))
                .Where(x => x.SpentOn >= from && x.SpentOn <= to)
                .Select(x => new ProjectFinance.CostItem(CostSource.Expenses, x.Currency, x.Amount, x.SpentOn)),
            rates);

        var finance = await projectFinance.GetAsync(projects.Values.ToList(), cancellationToken);
        return new ReportMoneyResponse(
            labour,
            expenses.Expenses,
            expenses.Unconverted.Select(u => new CurrencyAmountResponse(u.Currency, u.Amount)).ToList(),
            labour.Usd + expenses.Expenses,
            projects.Values.Where(p => p.BillingType == BillingType.Client).Sum(p => finance[p.Id].Summary.Profit),
            byProject
                .Select(p =>
                {
                    var summary = finance[p.ProjectId].Summary;
                    return new ProjectProfitResponse(p.ProjectId, p.Title, summary.Revenue, summary.Cost, summary.Profit);
                })
                .ToList(),
            views
                .GroupBy(e => e.UserId)
                .Select(g => new PersonLabourCostResponse(g.Key, g.First().UserName, pricer.Price(g.Select(ToRow))))
                .OrderByDescending(p => p.LabourCost.Usd)
                .ToList());
    }

    private static IEnumerable<ProjectMinutesResponse> ProjectMinutes(IEnumerable<WorkStats.Worked> worked, IReadOnlyDictionary<long, string> titles)
        => worked
            .GroupBy(w => w.ProjectId)
            .Select(g => new ProjectMinutesResponse(g.Key, titles.GetValueOrDefault(g.Key, ""), g.Sum(w => w.Minutes)))
            .OrderByDescending(p => p.Minutes);

    private WorkStats.Worked ToWorked(WorkEntryView e) => new(e.Id, e.UserId, e.ProjectId, clock.ToCompanyTime(e.StartedAt), e.Minutes);

    private static WorkRow ToRow(WorkEntryView e) => new(e.Id, e.UserId, e.ProjectId, e.StartedAt, e.EndedAt, e.Minutes, e.CreatedAt);

    private static int SharePct(int minutes, int total) => total > 0 ? (int)Math.Round(minutes * 100m / total, MidpointRounding.AwayFromZero) : 0;

    private static IReadOnlyList<WeekTotalResponse> ToWeeks(IReadOnlyList<WorkStats.WeekTotal> weeks)
        => weeks.Select(w => new WeekTotalResponse(w.WeekStart, w.Minutes, w.Entries)).ToList();

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
