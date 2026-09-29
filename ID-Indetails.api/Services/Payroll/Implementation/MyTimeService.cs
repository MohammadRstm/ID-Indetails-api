using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
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

    private async Task<User> MeAsync(CancellationToken cancellationToken)
        => (await payroll.UsersAsync([currentUser.Id], cancellationToken)).Single();
}
