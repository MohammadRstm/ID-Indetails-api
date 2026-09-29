using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Payroll.Implementation;

/// <summary>
/// The caller's own working time, and someone else's hours and overtime for people who may see them (a person of a
/// domain the caller can't see reads as not found). Calculated the same way as Salaries (see <see cref="IPayrollInputs"/>).
/// </summary>
public sealed class MyTimeService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IPayrollRepository payroll,
    IPayrollInputs inputs,
    IScheduleService schedules,
    IWorkingTimeService workingTime,
    IWorkEntryRepository entries,
    IClock clock) : IMyTimeService
{
    public async Task<MyScheduleResponse> ScheduleAsync(MyScheduleQuery query, CancellationToken cancellationToken = default)
    {
        var me = await MeAsync(cancellationToken);
        var day = query.Date ?? clock.CompanyToday;
        var input = (await inputs.ForAsync([me], [new DateOnly(day.Year, day.Month, 1)], cancellationToken))[0].Input;
        var hours = input.Calendar.HoursOn(day);
        var monthWork = Overtime.Of(input.Work, input.Calendar);
        var work = monthWork.Where(w => w.Day == day).ToList();
        var logged = work.Sum(w => w.Minutes);
        var overtime = work.Sum(w => w.Overtime);

        return new MyScheduleResponse(
            await schedules.DescribeAsync(me, cancellationToken),
            (await workingTime.GetAsync(cancellationToken)).Current,
            new MyScheduleDayResponse(
                day,
                hours is not null,
                hours?.Start,
                hours?.End,
                hours?.Required ?? 0,
                hours?.PaidBreak ?? 0,
                logged,
                logged - overtime,
                overtime,
                hours is null ? 0 : Math.Max(0, hours.Required - (logged - overtime)),
                input.LedgerApplies ? SalaryCalculator.LedgerOf(input, monthWork).Short : null));
    }

    public async Task<MyHoursResponse> HoursAsync(MyHoursQuery query, CancellationToken cancellationToken = default)
        => await HoursOfAsync(await MeAsync(cancellationToken), query, cancellationToken);

    public async Task<MyHoursResponse> HoursForPersonAsync(long userId, MyHoursQuery query, CancellationToken cancellationToken = default)
        => await HoursOfAsync((await PersonAsync(userId, cancellationToken)).Person, query, cancellationToken);

    public async Task<MyOvertimeResponse> OvertimeAsync(SalaryMonthQuery query, CancellationToken cancellationToken = default)
        => await OvertimeOfAsync(await MeAsync(cancellationToken), query, withLedger: true, cancellationToken);

    public async Task<MyOvertimeResponse> OvertimeForPersonAsync(long userId, SalaryMonthQuery query, CancellationToken cancellationToken = default)
    {
        var (person, actor) = await PersonAsync(userId, cancellationToken);

        // Which overtime made up missing hours is pay information, as in the prototype where only super admins see it.
        return await OvertimeOfAsync(person, query, withLedger: actor.Has(PermissionCodes.SalaryView), cancellationToken);
    }

    private async Task<MyHoursResponse> HoursOfAsync(User person, MyHoursQuery query, CancellationToken cancellationToken)
    {
        if (!PayrollInputs.LedgerApplies(person))
        {
            return new MyHoursResponse(false, query.Page, MyTime.MonthsPerPage, 0, []);
        }

        var (months, total) = MyTime.MonthsPage(inputs.JoinedOn(person), clock.CompanyToday, query.Page, inputs.LeftOn(person));
        var ledgers = (await inputs.ForAsync([person], months, cancellationToken))
            .Select(m => SelfService.LedgerMonth(m, SalaryCalculator.LedgerOf(m.Input, Overtime.Of(m.Input.Work, m.Input.Calendar))))
            .ToList();

        return new MyHoursResponse(true, query.Page, MyTime.MonthsPerPage, total, ledgers);
    }

    /// <param name="withLedger">Whether to say which overtime made up missing hours and which is paid as overtime.</param>
    private async Task<MyOvertimeResponse> OvertimeOfAsync(User person, SalaryMonthQuery query, bool withLedger, CancellationToken cancellationToken)
    {
        var month = SelfService.MonthOf(query.Month, clock.CompanyToday, inputs.JoinedOn(person));
        var input = (await inputs.ForAsync([person], [month], cancellationToken))[0].Input;
        var work = Overtime.Of(input.Work, input.Calendar);
        var ledger = withLedger && input.LedgerApplies ? SalaryCalculator.LedgerOf(input, work) : null;
        var overtime = work.Where(w => w.Overtime > 0).ToList();
        var views = (await entries.ViewsByIdAsync(overtime.Select(w => w.Id).ToList(), cancellationToken)).ToDictionary(v => v.Id);

        // An entry deleted between the two reads is left out rather than failing the request.
        overtime = overtime.Where(w => views.ContainsKey(w.Id)).ToList();
        var looks = await payroll.ProjectLooksAsync(views.Values.Select(v => v.ProjectId).Distinct().ToList(), cancellationToken);

        return new MyOvertimeResponse(
            month,
            clock.CompanyToday < month.AddMonths(1),
            overtime.Sum(w => w.Overtime),
            ledger?.MadeUp,
            ledger?.OvertimePaid,
            overtime.Count,
            overtime.GroupBy(w => w.Day)
                .OrderByDescending(g => g.Key)
                .Select(g => new MyOvertimeDayResponse(
                    g.Key,
                    input.Calendar.HoursOn(g.Key) is null,
                    g.Sum(w => w.Overtime),
                    g.Select(w => views[w.Id])
                        .OrderBy(v => v.StartedAt)
                        .Select(v => new MyOvertimeEntryResponse(
                            v.Id,
                            v.ProjectId,
                            v.ProjectTitle,
                            looks.GetValueOrDefault(v.ProjectId)?.Icon,
                            looks.GetValueOrDefault(v.ProjectId)?.Tint,
                            v.Description,
                            v.StartedAt,
                            v.EndedAt,
                            v.Minutes,
                            g.Single(w => w.Id == v.Id).Overtime,
                            ledger?.MadeUpByWork.GetValueOrDefault(v.Id)))
                        .ToList()))
                .ToList());
    }

    private async Task<User> MeAsync(CancellationToken cancellationToken)
        => (await payroll.UsersAsync([currentUser.Id], cancellationToken)).Single();

    private async Task<(User Person, UserAccess Actor)> PersonAsync(long userId, CancellationToken cancellationToken)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var person = (await payroll.UsersAsync([userId], cancellationToken)).SingleOrDefault();
        return person is not null && EmployeeRules.CanSee(actor, person.DomainCode)
            ? (person, actor)
            : throw new NotFoundException("Employee not found.");
    }
}
