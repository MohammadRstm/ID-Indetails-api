using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;

namespace ID_Indetails.api.Services.Payroll.Abstraction;

/// <summary>The caller's own working time: their schedule, hours ledger and overtime.</summary>
public interface IMyTimeService
{
    Task<MyScheduleResponse> ScheduleAsync(MyScheduleQuery query, CancellationToken cancellationToken = default);

    /// <summary>The hours ledger month by month, newest first; Design employees only.</summary>
    Task<MyHoursResponse> HoursAsync(MyHoursQuery query, CancellationToken cancellationToken = default);

    Task<MyOvertimeResponse> OvertimeAsync(SalaryMonthQuery query, CancellationToken cancellationToken = default);

    /// <summary>Someone's hours ledger, as they see it themselves; up to the month they left.</summary>
    Task<MyHoursResponse> HoursForPersonAsync(long userId, MyHoursQuery query, CancellationToken cancellationToken = default);

    /// <summary>Someone's overtime in a month. What made up missing hours is only given to callers with salary:view.</summary>
    Task<MyOvertimeResponse> OvertimeForPersonAsync(long userId, SalaryMonthQuery query, CancellationToken cancellationToken = default);
}
