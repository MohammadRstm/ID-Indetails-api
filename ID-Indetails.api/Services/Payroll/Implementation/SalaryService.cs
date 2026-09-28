using System.Globalization;
using System.Text;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
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
    IScheduleRepository schedules,
    IWorkingTimeService workingTime,
    IClock clock) : ISalaryService
{
    private sealed record Computed(User User, SalaryCalculator.Result Result, IReadOnlyList<HourExcuse> Excuses);

    public async Task<SalariesResponse> ListAsync(SalaryListQuery query, CancellationToken cancellationToken = default)
    {
        var (month, computed) = await CalculateAsync(query.Month, query.Domain, query.Search, null, cancellationToken);
        var rows = computed.Select(c => ToRow(c)).ToList();
        var policy = (await PoliciesAsync(cancellationToken)).LastOrDefault(p => p.EffectiveFrom <= clock.CompanyToday);

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
                rows.Sum(r => r.ShortMinutes ?? 0)));
    }

    public async Task<SalaryDetailResponse> GetAsync(long userId, SalaryMonthQuery query, CancellationToken cancellationToken = default)
    {
        var (month, computed) = await CalculateAsync(query.Month, null, null, userId, cancellationToken);
        var person = computed.SingleOrDefault() ?? throw new NotFoundException("No salary for this person in that month.");
        var result = person.Result;
        int MadeUp(Overtime.Split w) => result.Ledger?.MadeUpByWork.GetValueOrDefault(w.Id) ?? 0;

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
            person.Excuses.Select(HourExcuseService.ToResponse).ToList());
    }

    public async Task<(string FileName, string Csv)> ExportAsync(SalaryListQuery query, CancellationToken cancellationToken = default)
    {
        var (month, computed) = await CalculateAsync(query.Month, query.Domain, query.Search, null, cancellationToken);
        var rows = computed.Select(c => ToRow(c)).ToList();
        var csv = new StringBuilder();
        var monthText = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        Line(csv, "Month", "Employee", "Email", "Domain", "Team", "Pay basis", "Monthly salary", "Hourly rate", "Currency",
            "Expected hours", "Hours in schedule", "Overtime made up", "Overtime paid", "Excused hours", "Short hours", "Flagged",
            "Pro-rated", "Base pay", "Deduction", "Overtime pay", "To pay");

        foreach (var r in rows)
        {
            Line(csv, monthText, r.Name, r.Email, r.Domain, r.Team?.Name, r.Basis.ToString().ToLowerInvariant(),
                r.Basis == PayBasis.Monthly ? Money(r.Amount) : null, Money(r.HourlyRate), r.Currency,
                Hours(r.ExpectedMinutes), Hours(r.NormalMinutes), Hours(r.MadeUpMinutes), Hours(r.OvertimeMinutes),
                Hours(r.ExcusedMinutes), Hours(r.ShortMinutes), r.Flagged ? "yes" : r.Behind ? "behind so far" : "no",
                r.ProRated ? $"{r.WorkingDaysPaid}/{r.WorkingDays} days" : "no",
                Money(r.BasePay), Money(r.Deduction), Money(r.OvertimePay), Money(r.Total));
        }

        foreach (var t in Totals(rows))
        {
            Line(csv, monthText, "TOTAL", null, null, null, null, null, null, t.Currency, null, null, null, null, null, null, null, null,
                Money(t.BasePay), Money(t.Deduction), Money(t.OvertimePay), Money(t.Total));
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

        var people = (await payroll.PeopleAsync(actor.VisibleDomains, EmployeeRules.CanSee(actor, null), domain, search?.Trim(), cancellationToken))
            .Where(p => onlyUserId is null || p.Id == onlyUserId)
            .ToList();
        var ids = people.Select(p => p.Id).ToList();
        var next = month.AddMonths(1);

        var pay = (await payroll.PayHistoryAsync(ids, cancellationToken)).ToLookup(p => p.UserId);
        var personal = (await schedules.SchedulesAsync(ids, cancellationToken)).ToLookup(s => s.UserId);
        var excuses = (await payroll.ExcusesAsync(ids, month, cancellationToken)).ToLookup(x => x.UserId);
        var policies = (await PoliciesAsync(cancellationToken)).ToList();

        // A day either side covers the time-zone offset; entries are then kept by their company date.
        var work = (await payroll.WorkAsync(
                ids,
                month.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                next.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                cancellationToken))
            .Select(e => (e.UserId, Local: clock.ToCompanyTime(e.StartedAt), e.Id, e.Minutes))
            .Where(e => DateOnly.FromDateTime(e.Local) >= month && DateOnly.FromDateTime(e.Local) < next)
            .ToLookup(e => e.UserId, e => new Overtime.Work(e.Id, DateOnly.FromDateTime(e.Local), (int)e.Local.TimeOfDay.TotalMinutes, e.Minutes));

        var computed = new List<Computed>();
        foreach (var person in people)
        {
            var result = SalaryCalculator.Calculate(new SalaryCalculator.Input(
                month,
                today,
                person.JoinedOn!.Value,
                person.DomainCode == DomainCodes.Design && person.RoleCode == RoleCodes.Employee,
                pay[person.Id].Select(p => new SalaryCalculator.PayTerms(p.EffectiveFrom, p.Basis, p.Amount, p.Currency, p.HourlyCostRate)).ToList(),
                new PersonCalendar(policies, personal[person.Id].Select(s => new PersonCalendar.Schedule(
                    s.EffectiveFrom, s.FollowsDefault, s.Days.ToDictionary(d => (int)d.Weekday, d => ((int)d.StartMinute, (int)d.EndMinute))))),
                work[person.Id].ToList(),
                excuses[person.Id].Select(x => new SalaryCalculator.Excuse(x.Kind, x.Minutes, x.Days.Select(d => d.Day).ToList(), x.IsPaid)).ToList()));

            if (result is not null)
            {
                computed.Add(new Computed(person, result, excuses[person.Id].ToList()));
            }
        }

        return (month, computed);
    }

    private async Task<IReadOnlyList<PersonCalendar.Policy>> PoliciesAsync(CancellationToken cancellationToken)
        => (await workingTime.GetPoliciesAsync(cancellationToken))
            .Select(p => new PersonCalendar.Policy(p.EffectiveFrom, p.WorkDays, p.WorkStartMinute, p.WorkEndMinute, p.BreakMinutes, p.OvertimeMultiplier))
            .ToList();

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
            TracksHours = user.DomainCode != DomainCodes.Execution,
            ExpectedMinutes = r.Ledger?.Expected,
            NormalMinutes = r.NormalMinutes,
            MadeUpMinutes = r.MadeUpMinutes,
            OvertimeMinutes = r.OvertimePaidMinutes,
            ExcusedMinutes = r.ExcusedMinutes,
            ShortMinutes = r.Ledger?.Short,
            Flagged = r.Ledger?.Flagged ?? false,
            Behind = r.Ledger?.Behind ?? false,
            ProRated = r.ProRated,
            WorkingDays = r.WorkingDays,
            WorkingDaysPaid = r.WorkingDaysPaid,
            Deduction = r.Deduction,
            BasePay = r.BasePay,
            OvertimePay = r.OvertimePay,
            Total = r.Total,
        };
    }

    private static IReadOnlyList<SalaryTotalResponse> Totals(IEnumerable<SalaryRowResponse> rows)
        => rows.GroupBy(r => r.Currency)
            .OrderBy(g => g.Key)
            .Select(g => new SalaryTotalResponse(g.Key, g.Count(), g.Sum(r => r.BasePay), g.Sum(r => r.OvertimePay), g.Sum(r => r.Deduction), g.Sum(r => r.Total)))
            .ToList();

    private static string? Hours(int? minutes) => minutes is { } m ? (m / 60m).ToString("0.##", CultureInfo.InvariantCulture) : null;

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static void Line(StringBuilder csv, params string?[] fields)
        => csv.AppendLine(string.Join(',', fields.Select(Field)));

    private static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // A leading =, +, - or @ would run as a formula in a spreadsheet (names are typed by users).
        if ("=+-@".Contains(value[0]) && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
