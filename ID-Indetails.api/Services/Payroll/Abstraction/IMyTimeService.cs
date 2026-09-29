using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;

namespace ID_Indetails.api.Services.Payroll.Abstraction;

/// <summary>The caller's own working time: their schedule, hours ledger and overtime.</summary>
public interface IMyTimeService
{
    Task<MyScheduleResponse> ScheduleAsync(CancellationToken cancellationToken = default);

    /// <summary>The hours ledger month by month, newest first; Design employees only.</summary>
    Task<MyHoursResponse> HoursAsync(MyHoursQuery query, CancellationToken cancellationToken = default);

    Task<MyOvertimeResponse> OvertimeAsync(SalaryMonthQuery query, CancellationToken cancellationToken = default);
}
