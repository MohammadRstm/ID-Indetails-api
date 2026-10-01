using System.Globalization;
using System.Text;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Payroll.Implementation;

/// <summary>
/// Salaries of everyone with a pay record in the caller's domains, calculated for a month from pay
/// history, working time, schedules, work entries and excuses (one query per source).
/// </summary>
public sealed class SalaryService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IPayrollRepository payroll,
    IPayrollInputs inputs,
    IClock clock) : ISalaryService
{
    private sealed record Computed(User User, SalaryCalculator.Result Result, IReadOnlyList<HourExcuse> Excuses, SalaryCalculator.Input Input);

    public async Task<SalariesResponse> ListAsync(SalaryListQuery query, CancellationToken cancellationToken = default)
    {
        var (month, computed) = await CalculateAsync(query.Month, query.Domain, query.Search, null, cancellationToken);
        var rows = computed.Select(c => ToRow(c)).ToList();
        var policy = (await inputs.PoliciesAsync(cancellationToken)).LastOrDefault(p => p.EffectiveFrom <= clock.CompanyToday);

        return new SalariesResponse(
            month,
            InProgress(month),
            policy?.OvertimeMultiplier ?? 1m,
            new PagedResponse<SalaryRowResponse>(
                rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), query.Page, query.PageSize, rows.Count),
            Totals(rows),
            new SalarySummaryResponse(
                rows.Count,
                rows.Count(r => r.Basis == PayBasis.Monthly),
                rows.Sum(r => r.NormalMinutes),
                computed.Sum(c => c.Result.OvertimeMinutes),
                rows.Sum(r => r.MadeUpMinutes),
                rows.Count(r => r.ShortMinutes > 0),
                rows.Sum(r => r.ShortMinutes ?? 0),
                rows.Any(r => r.ExpectedMinutes is not null) ? rows.Sum(r => r.ExpectedMinutes ?? 0) : null));
    }

    public async Task<SalaryDetailResponse> GetAsync(long userId, SalaryMonthQuery query, CancellationToken cancellationToken = default)
    {
        var (month, computed) = await CalculateAsync(query.Month, null, null, userId, cancellationToken);
        var person = computed.SingleOrDefault() ?? throw new NotFoundException("No salary for this person in that month.");
        var (result, input) = (person.Result, person.Input);
        int MadeUp(Overtime.Split w) => result.Ledger?.MadeUpByWork.GetValueOrDefault(w.Id) ?? 0;
        var workingDays = HoursLedger.Days(month, month.AddMonths(1))
            .Where(d => d >= input.JoinedOn && !(d > input.LeftOn) && !Employment.InGap(input.Gaps, d))
            .Select(d => (Day: d, Hours: input.Calendar.HoursOn(d)))
            .Where(d => d.Hours is not null)
            .Select(d => new SalaryDayResponse(d.Day, d.Hours!.Required))
            .ToList();

        return new SalaryDetailResponse(
            ToRow(person),
            InProgress(month),
            result.PaidBreakDays,
            result.PaidBreakPay,
            result.DeductionMinutes,
            result.Ledger?.MissedDays ?? [],
            result.Work.Where(w => w.Overtime > 0)
                .Select(w => new OvertimeWorkResponse(w.Id, w.Day, w.Minutes, w.Overtime, MadeUp(w)))
                .ToList(),
            person.Excuses.Select(HourExcuseService.ToResponse).ToList(),
            workingDays,
            await ProjectsAsync(userId, month, result, input, cancellationToken));
    }

    /// <summary>The month's work grouped by project, each entry paid as Project pay pays it.</summary>
    private async Task<IReadOnlyList<SalaryProjectResponse>> ProjectsAsync(
        long userId,
        DateOnly month,
        SalaryCalculator.Result result,
        SalaryCalculator.Input input,
        CancellationToken cancellationToken)
    {
        if (result.Work.Count == 0)
        {
            return [];
        }

        // A day either side covers the time-zone offset, as the salary's own work does.
        var projectOf = (await payroll.WorkAsync(
                [userId],
                month.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                month.AddMonths(1).AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                cancellationToken))
            .ToDictionary(e => e.Id, e => e.ProjectId);
        var priced = result.Work
            .Where(w => projectOf.ContainsKey(w.Id))
            .Select(w =>
            {
                var madeUp = result.Ledger?.MadeUpByWork.GetValueOrDefault(w.Id) ?? 0;
                var terms = SalaryCalculator.TermsOn(input.PayHistory, w.Day);
                return (ProjectId: projectOf[w.Id], Work: w, MadeUp: madeUp,
                    Pay: ProjectPay.Of(w, madeUp, terms, input.Calendar.PolicyOn(w.Day).OvertimeMultiplier));
            })
            .ToList();

        var ids = priced.Select(p => p.ProjectId).Distinct().ToList();
        var titles = (await payroll.ProjectsAsync(DomainCodes.Design, cancellationToken))
            .Where(p => ids.Contains(p.Id))
            .ToDictionary(p => p.Id, p => p.Title);
        var looks = await payroll.ProjectLooksAsync(ids, cancellationToken);

        return priced
            .GroupBy(p => (p.ProjectId, p.Pay.Currency))
            .Select(g => new SalaryProjectResponse(
                g.Key.ProjectId,
                titles.GetValueOrDefault(g.Key.ProjectId, "Project"),
                looks.GetValueOrDefault(g.Key.ProjectId)?.Icon,
                looks.GetValueOrDefault(g.Key.ProjectId)?.Tint,
                g.Count(),
                g.Sum(p => p.Work.Normal),
                g.Sum(p => p.MadeUp),
                g.Sum(p => p.Work.Overtime - p.MadeUp),
                Math.Round(g.Sum(p => p.Pay.Pay), 2, MidpointRounding.AwayFromZero),
                g.Key.Currency))
            .OrderByDescending(p => p.NormalMinutes + p.MadeUpMinutes + p.OvertimeMinutes)
            .ThenBy(p => p.Title)
            .ToList();
    }

    public async Task<MyPayResponse> GetMineAsync(SalaryMonthQuery query, CancellationToken cancellationToken = default)
    {
        var me = (await payroll.UsersAsync([currentUser.Id], cancellationToken)).Single();
        var today = clock.CompanyToday;
        var joinedOn = inputs.EmploymentOf(me).JoinedOn;
        var month = SelfService.MonthOf(query.Month, today, joinedOn);
        var personMonth = (await inputs.ForAsync([me], [month], cancellationToken))[0];
        var input = personMonth.Input;
        var work = Overtime.Of(input.Work, input.Calendar);
        var ledger = input.LedgerApplies ? SalaryCalculator.LedgerOf(input, work) : null;
        var pay = SalaryCalculator.Calculate(input);
        var excuses = personMonth.Excuses;
        var days = MyTime.Days(
            month,
            today,
            joinedOn,
            input.Calendar,
            work,
            excuses.SelectMany(x => x.Days).Select(d => d.Day).ToHashSet(),
            excuses.Any(x => x.Kind == ExcuseKind.WholeMonth));

        return new MyPayResponse(
            month,
            InProgress(month),
            joinedOn,
            new DateOnly(joinedOn.Year, joinedOn.Month, 1),
            pay is null ? null : MySalary(pay, input, InProgress(month)),
            ledger is null ? null : SelfService.LedgerMonth(personMonth, ledger),
            days.Select(d => new MyPayDayResponse(d.Date, d.Required, d.Logged, d.Inside, d.Overtime, d.Excused, d.DayOff, d.IsToday, d.Difference)).ToList(),
            excuses.Select(SelfService.Excuse).ToList());
    }

    public async Task<(string FileName, string Csv)> ExportAsync(SalaryListQuery query, CancellationToken cancellationToken = default)
    {
        var (month, computed) = await CalculateAsync(query.Month, query.Domain, query.Search, null, cancellationToken);
        var rows = computed.Select(c => ToRow(c)).ToList();
        var csv = new StringBuilder();
        var monthText = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        Csv.Line(csv, "Month", "Employee", "Email", "Domain", "Team", "Pay basis", "Monthly salary", "Hourly rate", "Currency",
            "Expected hours", "Hours in schedule", "Overtime made up", "Overtime paid", "Excused hours", "Short hours", "Flagged",
            "Pro-rated", "Base pay", "Deduction", "Overtime pay", "To pay");

        foreach (var r in rows)
        {
            Csv.Line(csv, monthText, r.Name, r.Email, r.Domain, r.Team?.Name, r.Basis.ToString().ToLowerInvariant(),
                r.Basis == PayBasis.Monthly ? Csv.Money(r.Amount) : null, Csv.Money(r.HourlyRate), r.Currency,
                Csv.Hours(r.ExpectedMinutes), Csv.Hours(r.NormalMinutes), Csv.Hours(r.MadeUpMinutes), Csv.Hours(r.OvertimeMinutes),
                Csv.Hours(r.ExcusedMinutes), Csv.Hours(r.ShortMinutes), r.Flagged ? "yes" : r.Behind ? "behind so far" : "no",
                r.ProRated ? $"{r.WorkingDaysPaid}/{r.WorkingDays} days" : "no",
                Csv.Money(r.BasePay), Csv.Money(r.Deduction), Csv.Money(r.OvertimePay), Csv.Money(r.Total));
        }

        foreach (var t in Totals(rows))
        {
            Csv.Line(csv, monthText, "TOTAL", null, null, null, null, null, null, t.Currency, null, null, null, null, null, null, null, null,
                Csv.Money(t.BasePay), Csv.Money(t.Deduction), Csv.Money(t.OvertimePay), Csv.Money(t.Total));
        }

        var domain = query.Domain is null ? string.Empty : "-" + query.Domain;
        return ($"salaries-{monthText}{domain}.csv", csv.ToString());
    }

    private async Task<(DateOnly Month, IReadOnlyList<Computed> People)> CalculateAsync(
        string? monthText,
        string? domain,
        string? search,
        long? onlyUserId,
        CancellationToken cancellationToken)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var today = clock.CompanyToday;
        var month = monthText is null
            ? new DateOnly(today.Year, today.Month, 1)
            : DateOnly.ParseExact(monthText + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (month > today)
        {
            throw new RequestValidationException(ErrorCodes.SalaryMonthInFuture, "month", "Salaries can't be shown for a future month.");
        }

        var people = (await payroll.PeopleAsync(
                actor.VisibleDomains, EmployeeRules.CanSee(actor, null), domain, search?.Trim(), month, cancellationToken))
            .Where(p => onlyUserId is null || p.Id == onlyUserId)
            .ToList();

        return (month, await ComputeAsync(month, people, cancellationToken));
    }

    public async Task<IReadOnlyList<ShortPerson>> FlaggedAsync(DateOnly month, CancellationToken cancellationToken = default)
    {
        var people = await payroll.PeopleAsync([DomainCodes.Design], false, DomainCodes.Design, null, month, cancellationToken);
        return (await ComputeAsync(month, people, cancellationToken))
            .Where(c => c.Result.Ledger is { Flagged: true })
            .Select(c => new ShortPerson(c.User.Id, ToRow(c).Name, c.Result.Ledger!.Short))
            .OrderByDescending(p => p.ShortMinutes)
            .ToList();
    }

    private async Task<IReadOnlyList<Computed>> ComputeAsync(DateOnly month, IReadOnlyList<User> people, CancellationToken cancellationToken)
    {
        var computed = new List<Computed>();
        foreach (var personMonth in await inputs.ForAsync(people, [month], cancellationToken))
        {
            if (SalaryCalculator.Calculate(personMonth.Input) is { } result)
            {
                computed.Add(new Computed(personMonth.User, result, personMonth.Excuses, personMonth.Input));
            }
        }

        return computed;
    }

    private bool InProgress(DateOnly month) => clock.CompanyToday >= month && clock.CompanyToday < month.AddMonths(1);

    private static SalaryRowResponse ToRow(Computed c)
    {
        var (user, r) = (c.User, c.Result);
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return new SalaryRowResponse
        {
            UserId = user.Id,
            Name = name.Length > 0 ? name : user.Email,
            Email = user.Email,
            Role = user.RoleCode,
            Domain = user.DomainCode,
            Team = user.Team is null ? null : new TeamSummaryResponse(user.Team.Id, user.Team.Name),
            Basis = r.Terms.Basis,
            Amount = r.Terms.Amount,
            Currency = r.Terms.Currency,
            HourlyRate = r.Terms.HourlyRate,
            // Design's people log hours; Execution staff and super admins are paid their salary
            TracksHours = user.DomainCode == DomainCodes.Design,
            ExpectedMinutes = r.Ledger?.Expected,
            NormalMinutes = r.NormalMinutes,
            MadeUpMinutes = r.MadeUpMinutes,
            OvertimeMinutes = r.OvertimePaidMinutes,
            ExcusedMinutes = r.ExcusedMinutes,
            ShortMinutes = r.Ledger?.Short,
            Flagged = r.Ledger?.Flagged ?? false,
            Behind = r.Ledger?.Behind ?? false,
            ProRated = r.ProRated,
            LeftOn = c.Input.LeftOn,
            PhotoFileId = user.PhotoFileId,
            Position = user.Position,
            WorkingDays = r.WorkingDays,
            WorkingDaysPaid = r.WorkingDaysPaid,
            Deduction = r.Deduction,
            BasePay = r.BasePay,
            OvertimePay = r.OvertimePay,
            Total = r.Total,
        };
    }

    private MySalaryResponse MySalary(SalaryCalculator.Result pay, SalaryCalculator.Input input, bool inProgress)
    {
        var lastDay = inProgress ? clock.CompanyToday : input.Month.AddMonths(1).AddDays(-1);
        var estimate = MyTime.EstimateOf(pay, input, inProgress);

        return new MySalaryResponse
        {
            Basis = pay.Terms.Basis,
            Amount = pay.Terms.Amount,
            Currency = pay.Terms.Currency,
            HourlyRate = pay.Terms.HourlyRate,
            OvertimeMultiplier = input.Calendar.PolicyOn(lastDay).OvertimeMultiplier,
            NormalMinutes = pay.NormalMinutes,
            MadeUpMinutes = pay.MadeUpMinutes,
            OvertimeMinutes = pay.OvertimePaidMinutes,
            ExcusedMinutes = pay.ExcusedMinutes,
            ProRated = pay.ProRated,
            WorkingDays = pay.WorkingDays,
            WorkingDaysPaid = pay.WorkingDaysPaid,
            PaidBreakDays = pay.PaidBreakDays,
            PaidBreakPay = pay.PaidBreakPay,
            DeductionMinutes = pay.DeductionMinutes,
            Deduction = pay.Deduction,
            BasePay = pay.BasePay,
            OvertimePay = pay.OvertimePay,
            Total = pay.Total,
            EarnedSoFar = estimate.EarnedSoFar,
            FullMonthEstimate = estimate.FullMonth,
            WouldLose = estimate.WouldLose,
        };
    }

    private static IReadOnlyList<SalaryTotalResponse> Totals(IEnumerable<SalaryRowResponse> rows)
        => rows.GroupBy(r => r.Currency)
            .OrderBy(g => g.Key)
            .Select(g => new SalaryTotalResponse(g.Key, g.Count(), g.Sum(r => r.BasePay), g.Sum(r => r.OvertimePay), g.Sum(r => r.Deduction), g.Sum(r => r.Total)))
            .ToList();
}
