using System.Globalization;
using System.Text;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Payroll.Implementation;

/// <summary>
/// Prices each Design work entry of the period. Overtime and made-up hours need the person's whole
/// day and month, so their work of every month the period touches is loaded (one query per source).
/// </summary>
public sealed class ProjectPayService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IPayrollRepository payroll,
    IPayrollInputs inputs,
    IScheduleRepository schedules,
    IWorkingTimeService workingTime,
    ICompanySettingsService settings,
    IClock clock) : IProjectPayService
{
    private sealed record PricedWork(long ProjectId, long UserId, DateOnly Day, ProjectPay.EntryPay Pay, PayBasis? Basis, decimal? MonthlySalary);

    public async Task<ProjectPayResponse> GetAsync(ProjectPayQuery query, CancellationToken cancellationToken = default)
    {
        var (from, until, projects, priced, users) = await CalculateAsync(query, cancellationToken);

        var groups = priced
            .GroupBy(w => w.ProjectId)
            .Select(g =>
            {
                var project = projects.Single(p => p.Id == g.Key);
                var people = g.GroupBy(w => (w.UserId, w.Pay.Currency))
                    .Select(p => Person(users[p.Key.UserId], p.ToList()))
                    .OrderByDescending(p => p.NormalMinutes + p.OvertimeMinutes)
                    .ThenBy(p => p.Name)
                    .ToList();

                return new ProjectPayProjectResponse(
                    project.Id,
                    project.Title,
                    ClientOf(project),
                    people.Sum(p => p.NormalMinutes),
                    people.Sum(p => p.OvertimeMinutes),
                    Totals(people),
                    people);
            })
            .OrderByDescending(p => p.Pay.Where(t => t.Currency == "USD").Sum(t => t.Total))
            .ThenByDescending(p => p.NormalMinutes + p.OvertimeMinutes)
            .ThenBy(p => p.Title)
            .ToList();

        return new ProjectPayResponse(
            query.Unit,
            from,
            until.AddDays(-1),
            projects.Count,
            groups,
            Totals(groups.SelectMany(g => g.People)),
            priced.Select(w => w.UserId).Distinct().Count(),
            groups.Sum(g => g.NormalMinutes),
            groups.Sum(g => g.OvertimeMinutes));
    }

    public async Task<(string FileName, string Csv)> ExportAsync(ProjectPayQuery query, CancellationToken cancellationToken = default)
    {
        var report = await GetAsync(query, cancellationToken);
        var period = query.Unit.ToString().ToLowerInvariant();
        var from = report.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = report.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var csv = new StringBuilder();

        Csv.Line(csv, "Period", "From", "To", "Project", "Client", "Team", "Employee", "Email", "Pay basis", "Entries",
            "Normal hours", "Overtime hours", "Total hours", "Hourly rate", "Currency", "Pay");

        foreach (var project in report.Projects)
        {
            foreach (var p in project.People)
            {
                Csv.Line(csv, period, from, to, project.Title, project.Client, p.Team?.Name, p.Name, p.Email,
                    p.Basis?.ToString().ToLowerInvariant(), p.Entries.ToString(CultureInfo.InvariantCulture),
                    Csv.Hours(p.NormalMinutes), Csv.Hours(p.OvertimeMinutes), Csv.Hours(p.NormalMinutes + p.OvertimeMinutes),
                    Csv.Money(p.HourlyRate), p.Currency, Csv.Money(p.Pay));
            }
        }

        var filtered = query.ProjectId is null ? string.Empty : "-filtered";
        return ($"project-pay-{period}-{from}{filtered}.csv", csv.ToString());
    }

    private async Task<(DateOnly From, DateOnly Until, IReadOnlyList<ProjectLabel> Projects, IReadOnlyList<PricedWork> Priced, IReadOnlyDictionary<long, Models.User> Users)>
        CalculateAsync(ProjectPayQuery query, CancellationToken cancellationToken)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        if (!actor.CanSee(DomainCodes.Design))
        {
            throw new NotFoundException("Project pay covers Design projects, which you can't see.");
        }

        var today = clock.CompanyToday;
        var weekStart = (DayOfWeek)(await settings.GetAsync(cancellationToken)).WeekStart;
        var (from, until) = ProjectPay.Period(query.Unit, query.Date ?? today, weekStart);
        if (from > today)
        {
            throw new RequestValidationException(ErrorCodes.ProjectPayPeriodInFuture, "date", "Project pay can't be shown for a future period.");
        }

        var allProjects = await payroll.ProjectsAsync(DomainCodes.Design, cancellationToken);
        var projects = query.ProjectId is { } id ? allProjects.Where(p => p.Id == id).ToList() : allProjects;
        if (query.ProjectId is not null && projects.Count == 0)
        {
            throw new NotFoundException("Project not found.");
        }

        var periodWork = (await payroll.WorkOnProjectsAsync(projects.Select(p => p.Id).ToList(), Utc(from.AddDays(-1)), Utc(until.AddDays(1)), cancellationToken))
            .Where(e => InRange(clock.ToCompanyDate(e.StartedAt), from, until))
            .ToList();
        var users = (await payroll.UsersAsync(periodWork.Select(e => e.UserId).Distinct().ToList(), cancellationToken))
            .Where(u => EmployeeRules.CanSee(actor, u.DomainCode))
            .ToDictionary(u => u.Id);
        var userIds = users.Keys.ToList();
        periodWork = periodWork.Where(e => users.ContainsKey(e.UserId)).ToList();
        var pay = (await payroll.PayHistoryAsync(userIds, cancellationToken)).ToLookup(p => p.UserId, p =>
            new SalaryCalculator.PayTerms(p.EffectiveFrom, p.Basis, p.Amount, p.Currency));
        var personal = (await schedules.SchedulesAsync(userIds, cancellationToken)).ToLookup(s => s.UserId);
        var policies = (await workingTime.GetPoliciesAsync(cancellationToken))
            .Select(p => new PersonCalendar.Policy(p.EffectiveFrom, p.WorkDays, p.WorkStartMinute, p.WorkEndMinute, p.BreakMinutes, p.OvertimeMultiplier))
            .ToList();

        // Overtime is decided per day and made-up hours per month, so every month the period touches is needed in full.
        var firstMonth = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(until.AddDays(-1).Year, until.AddDays(-1).Month, 1);
        var monthWork = (await payroll.WorkAsync(userIds, Utc(firstMonth.AddDays(-1)), Utc(lastMonth.AddMonths(1).AddDays(1)), cancellationToken))
            .Select(e => (e.UserId, Local: clock.ToCompanyTime(e.StartedAt), e.Id, e.Minutes))
            .Select(e => (e.UserId, Work: new Overtime.Work(e.Id, DateOnly.FromDateTime(e.Local), (int)e.Local.TimeOfDay.TotalMinutes, e.Minutes)))
            .Where(e => InRange(e.Work.Day, firstMonth, lastMonth.AddMonths(1)))
            .ToList();

        var splits = new Dictionary<long, (Overtime.Split Split, int MadeUp, PersonCalendar Calendar)>();
        for (var month = firstMonth; month <= lastMonth; month = month.AddMonths(1))
        {
            var excuses = (await payroll.ExcusesAsync(userIds, month, cancellationToken)).ToLookup(x => x.UserId);
            foreach (var userId in userIds)
            {
                var user = users[userId];
                var calendar = new PersonCalendar(policies, personal[userId].Select(s => new PersonCalendar.Schedule(
                    s.EffectiveFrom, s.FollowsDefault, s.Days.ToDictionary(d => (int)d.Weekday, d => ((int)d.StartMinute, (int)d.EndMinute)))));
                var work = Overtime.Of(
                    monthWork.Where(e => e.UserId == userId && e.Work.Day.Month == month.Month && e.Work.Day.Year == month.Year).Select(e => e.Work),
                    calendar);
                if (!PayrollInputs.PaidForHours(user))
                {
                    // a fixed salary earns no overtime: any time logged before admins stopped logging is priced as normal hours
                    work = work.Select(w => w with { Overtime = 0 }).ToList();
                }

                var employment = inputs.EmploymentOf(user);
                var ledger = user.DomainCode == DomainCodes.Design && user.RoleCode == RoleCodes.Employee
                    ? HoursLedger.Build(month, today, employment.JoinedOn, calendar, work, new HoursLedger.Excuses(
                        excuses[userId].Any(x => x.Kind == ExcuseKind.WholeMonth),
                        excuses[userId].Sum(x => x.Minutes ?? 0),
                        excuses[userId].SelectMany(x => x.Days.Select(d => d.Day)).ToHashSet()),
                        employment.LeftOn,
                        employment.Gaps)
                    : null;

                foreach (var split in work)
                {
                    splits[split.Id] = (split, ledger?.MadeUpByWork.GetValueOrDefault(split.Id) ?? 0, calendar);
                }
            }
        }

        var priced = periodWork
            .Where(e => splits.ContainsKey(e.Id))
            .Select(e =>
            {
                var (split, madeUp, calendar) = splits[e.Id];
                var terms = SalaryCalculator.TermsOn(pay[e.UserId], split.Day);
                return new PricedWork(
                    e.ProjectId,
                    e.UserId,
                    split.Day,
                    ProjectPay.Of(split, madeUp, terms, calendar),
                    terms?.Basis,
                    terms?.Basis == PayBasis.Monthly ? terms.Amount : null);
            })
            .ToList();

        return (from, until, allProjects, priced, users);
    }

    private static ProjectPayPersonResponse Person(Models.User user, IReadOnlyList<PricedWork> work)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        var latest = work.MaxBy(w => w.Day)!;
        return new ProjectPayPersonResponse(
            user.Id,
            name.Length > 0 ? name : user.Email,
            user.Email,
            user.Team is null ? null : new TeamSummaryResponse(user.Team.Id, user.Team.Name),
            latest.Basis,
            work.Count,
            work.Sum(w => w.Pay.Normal),
            work.Sum(w => w.Pay.Overtime),
            latest.Pay.HourlyRate,
            work.Select(w => w.Pay.HourlyRate).Distinct().Count() > 1,
            latest.Pay.Currency,
            Math.Round(work.Sum(w => w.Pay.Pay), 2, MidpointRounding.AwayFromZero),
            work.All(w => w.Pay.Priced),
            user.PhotoFileId,
            user.Position,
            latest.MonthlySalary);
    }

    private static IReadOnlyList<CurrencyTotalResponse> Totals(IEnumerable<ProjectPayPersonResponse> people)
        => people.GroupBy(p => p.Currency).OrderBy(g => g.Key).Select(g => new CurrencyTotalResponse(g.Key, g.Sum(p => p.Pay))).ToList();

    private static string? ClientOf(ProjectLabel project)
        => project.ClientName is null ? null : ClientRules.DisplayName(project.ClientName, project.ClientOrganisation);

    private static bool InRange(DateOnly day, DateOnly from, DateOnly until) => day >= from && day < until;

    private static DateTime Utc(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
