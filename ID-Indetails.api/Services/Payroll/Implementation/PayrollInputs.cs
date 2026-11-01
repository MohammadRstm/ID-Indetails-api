using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Payroll.Implementation;

public sealed class PayrollInputs(
    IPayrollRepository payroll,
    IScheduleRepository schedules,
    IWorkingTimeService workingTime,
    IClock clock) : IPayrollInputs
{
    /// <summary>The hours ledger applies to Design employees only: whether it applies to them today.</summary>
    public static bool LedgerApplies(User person) => person.DomainCode == DomainCodes.Design && person.RoleCode == RoleCodes.Employee;

    public Employment EmploymentOf(User person)
        => person.EmploymentPeriods.Count > 0
            ? new Employment(person.EmploymentPeriods
                .OrderBy(p => p.StartedOn)
                .Select(p => new Employment.Period(p.StartedOn, p.EndedOn))
                .ToList())
            : new Employment([new Employment.Period(person.JoinedOn ?? clock.ToCompanyDate(person.CreatedAt), null)]);

    public async Task<IReadOnlyList<PersonCalendar.Policy>> PoliciesAsync(CancellationToken cancellationToken = default)
        => (await workingTime.GetPoliciesAsync(cancellationToken))
            .Select(p => new PersonCalendar.Policy(p.EffectiveFrom, p.WorkDays, p.WorkStartMinute, p.WorkEndMinute, p.BreakMinutes, p.OvertimeMultiplier))
            .ToList();

    public async Task<IReadOnlyDictionary<long, PersonCalendar>> CalendarsAsync(
        IReadOnlyCollection<long> userIds,
        CancellationToken cancellationToken = default)
    {
        var personal = (await schedules.SchedulesAsync(userIds, cancellationToken)).ToLookup(s => s.UserId);
        var policies = await PoliciesAsync(cancellationToken);

        return userIds.Distinct().ToDictionary(id => id, id => new PersonCalendar(policies, personal[id].Select(s => new PersonCalendar.Schedule(
            s.EffectiveFrom, s.FollowsDefault, s.Days.ToDictionary(d => (int)d.Weekday, d => ((int)d.StartMinute, (int)d.EndMinute))))));
    }

    public async Task<IReadOnlyDictionary<long, RoleHistory>> RoleHistoriesAsync(
        IReadOnlyCollection<long> userIds,
        CancellationToken cancellationToken = default)
    {
        var changes = (await payroll.RoleChangesAsync(userIds, cancellationToken)).ToLookup(c => c.UserId);
        return (await payroll.RolesAsync(userIds, cancellationToken)).ToDictionary(
            r => r.Key,
            r => new RoleHistory(r.Value, changes[r.Key].Select(c => new RoleHistory.Change(clock.ToCompanyDate(c.OccurredAt), c.From, c.To)).ToList()));
    }

    public async Task<IReadOnlyList<PersonMonth>> ForAsync(
        IReadOnlyList<User> people,
        IReadOnlyList<DateOnly> months,
        CancellationToken cancellationToken = default)
    {
        if (people.Count == 0 || months.Count == 0)
        {
            return [];
        }

        var ids = people.Select(p => p.Id).ToList();
        var today = clock.CompanyToday;
        var pay = (await payroll.PayHistoryAsync(ids, cancellationToken)).ToLookup(p => p.UserId);
        var calendars = await CalendarsAsync(ids, cancellationToken);
        var roles = await RoleHistoriesAsync(ids, cancellationToken);

        var excuses = new Dictionary<DateOnly, ILookup<long, HourExcuse>>();
        foreach (var month in months.Distinct())
        {
            excuses[month] = (await payroll.ExcusesAsync(ids, month, cancellationToken)).ToLookup(x => x.UserId);
        }

        // A day either side covers the time-zone offset; entries are then kept by their company date.
        var work = (await payroll.WorkAsync(
                ids,
                months.Min().AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                months.Max().AddMonths(1).AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                cancellationToken))
            .Select(e => (e.UserId, Local: clock.ToCompanyTime(e.StartedAt), e.Id, e.Minutes))
            .ToLookup(e => e.UserId);

        var result = new List<PersonMonth>();
        foreach (var person in people)
        {
            var terms = pay[person.Id].Select(p => new SalaryCalculator.PayTerms(p.EffectiveFrom, p.Basis, p.Amount, p.Currency, p.OvertimeRate)).ToList();
            var calendar = calendars[person.Id];
            var employment = EmploymentOf(person);

            foreach (var month in months)
            {
                var next = month.AddMonths(1);
                var monthExcuses = excuses[month][person.Id].ToList();
                var fixedSalary = roles[person.Id].FixedSalaryMonth(month);
                result.Add(new PersonMonth(person, new SalaryCalculator.Input(
                    month,
                    today,
                    employment.JoinedOn,
                    person.DomainCode == DomainCodes.Design && !fixedSalary,
                    terms,
                    calendar,
                    work[person.Id]
                        .Where(e => DateOnly.FromDateTime(e.Local) >= month && DateOnly.FromDateTime(e.Local) < next)
                        .Select(e => new Overtime.Work(e.Id, DateOnly.FromDateTime(e.Local), (int)e.Local.TimeOfDay.TotalMinutes, e.Minutes))
                        .ToList(),
                    monthExcuses.Select(x => new SalaryCalculator.Excuse(x.Kind, x.Minutes, x.Days.Select(d => d.Day).ToList(), x.IsPaid)).ToList(),
                    employment.LeftOn,
                    employment.Gaps,
                    fixedSalary),
                    monthExcuses));
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<long, EntrySplit>> EntrySplitsAsync(
        IReadOnlyList<User> people,
        IReadOnlyList<DateOnly> months,
        CancellationToken cancellationToken = default)
    {
        var splits = new Dictionary<long, EntrySplit>();
        foreach (var personMonth in await ForAsync(people, months, cancellationToken))
        {
            var input = personMonth.Input;
            // a fixed salary earns no overtime: time logged in a month they were an admin counts as normal hours
            var work = Overtime.Of(input.Work, input.Calendar).Select(w => input.FixedSalary ? w with { Overtime = 0 } : w).ToList();
            var ledger = input.LedgerApplies ? SalaryCalculator.LedgerOf(input, work) : null;
            foreach (var w in work)
            {
                splits[w.Id] = new EntrySplit(w, ledger?.MadeUpByWork.GetValueOrDefault(w.Id) ?? 0);
            }
        }

        return splits;
    }
}
