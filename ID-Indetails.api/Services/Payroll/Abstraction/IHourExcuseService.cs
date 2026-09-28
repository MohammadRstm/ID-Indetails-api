using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Payroll;

namespace ID_Indetails.api.Services.Payroll.Abstraction;

public interface IHourExcuseService
{
    Task<HourExcuseResponse> AddAsync(long userId, AddHourExcuseRequest request, CancellationToken cancellationToken = default);

    /// <summary>Soft delete: the excuse stops counting but its history stays.</summary>
    Task DeleteAsync(long excuseId, CancellationToken cancellationToken = default);
}
