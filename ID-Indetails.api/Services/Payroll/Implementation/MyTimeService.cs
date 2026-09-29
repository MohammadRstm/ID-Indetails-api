using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Payroll.Implementation;

/// <summary>Always the caller's own data; calculated the same way as Salaries (see <see cref="IPayrollInputs"/>).</summary>
public sealed class MyTimeService(
    ICurrentUser currentUser,
    IPayrollRepository payroll,
    IPayrollInputs inputs,
    IScheduleService schedules,
    IWorkingTimeService workingTime,
    IWorkEntryRepository entries,
    IClock clock) : IMyTimeService
{
    public async Task<MyScheduleResponse> ScheduleAsync(CancellationToken cancellationToken = default)
    {
        var me = await MeAsync(cancellationToken);
        var today = clock.CompanyToday;
        var input = (await inputs.ForAsync([me], [new DateOnly(today.Year, today.Month, 1)], cancellationToken))[0].Input;
        var hours = input.Calendar.HoursOn(today);
        var work = Overtime.Of(input.Work.Where(w => w.Day == today), input.Calendar);
        var logged = work.Sum(w => w.Minutes);
        var overtime = work.Sum(w => w.Overtime);

        return new MyScheduleResponse(
            await schedules.DescribeAsync(me, cancellationToken),
            (await workingTime.GetAsync(cancellationToken)).Current,
            new MyTodayResponse(
                today,
                hours is not null,
                hours?.Start,
                hours?.End,
                hours?.Required ?? 0,
                hours?.PaidBreak ?? 0,
                logged,
                logged - overtime,
                overtime,
                hours is null ? 0 : Math.Max(0, hours.Required - (logged - overtime))));
    }

    public async Task<MyHoursResponse> HoursAsync(MyHoursQuery query, CancellationToken cancellationToken = default)
    {
        var me = await MeAsync(cancellationToken);
        if (!PayrollInputs.LedgerApplies(me))
        {
            return new MyHoursResponse(false, query.Page, MyTime.MonthsPerPage, 0, []);
        }

        var (months, total) = MyTime.MonthsPage(inputs.JoinedOn(me), clock.CompanyToday, query.Page);
        var ledgers = (await inputs.ForAsync([me], months, cancellationToken))
            .Select(m => SelfService.LedgerMonth(m, SalaryCalculator.LedgerOf(m.Input, Overtime.Of(m.Input.Work, m.Input.Calendar))))
            .ToList();

        return new MyHoursResponse(true, query.Page, MyTime.MonthsPerPage, total, ledgers);
    }

    public async Task<MyOvertimeResponse> OvertimeAsync(SalaryMonthQuery query, CancellationToken cancellationToken = default)
    {
        var me = await MeAsync(cancellationToken);
        var month = SelfService.MonthOf(query.Month, clock.CompanyToday, inputs.JoinedOn(me));
        var input = (await inputs.ForAsync([me], [month], cancellationToken))[0].Input;
        var work = Overtime.Of(input.Work, input.Calendar);
        var ledger = input.LedgerApplies ? SalaryCalculator.LedgerOf(input, work) : null;
        var overtime = work.Where(w => w.Overtime > 0).ToList();
        var views = (await entries.ViewsByIdAsync(overtime.Select(w => w.Id).ToList(), cancellationToken)).ToDictionary(v => v.Id);

        // An entry deleted between the two reads is left out rather than failing the request.
        overtime = overtime.Where(w => views.ContainsKey(w.Id)).ToList();

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
}
