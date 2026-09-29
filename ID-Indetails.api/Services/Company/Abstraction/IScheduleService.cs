using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Company.Abstraction;

public interface IScheduleService
{
    Task<SchedulesResponse> ListAsync(ScheduleListQuery query, CancellationToken cancellationToken = default);

    /// <summary>One person's week as it stands today, and their next change if one is set.</summary>
    Task<PersonScheduleResponse> DescribeAsync(User user, CancellationToken cancellationToken = default);

    Task<PersonScheduleResponse> SetAsync(long userId, SetScheduleRequest request, CancellationToken cancellationToken = default);
}
