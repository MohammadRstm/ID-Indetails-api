using ID_Indetails.api.DTO.Response.Payroll;

namespace ID_Indetails.api.Services.Payroll.Abstraction;

/// <summary>The caller's own working time: their schedule, hours ledger and overtime.</summary>
public interface IMyTimeService
{
    Task<MyScheduleResponse> ScheduleAsync(CancellationToken cancellationToken = default);
}
