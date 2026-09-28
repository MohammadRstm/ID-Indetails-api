using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Company.Abstraction;

public interface IScheduleService
{
    Task<SchedulesResponse> ListAsync(ScheduleListQuery query, CancellationToken cancellationToken = default);

    Task<PersonScheduleResponse> SetAsync(long userId, SetScheduleRequest request, CancellationToken cancellationToken = default);
}
