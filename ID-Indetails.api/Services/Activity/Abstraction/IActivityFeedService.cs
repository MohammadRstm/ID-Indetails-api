using ID_Indetails.api.DTO.Request.Activity;
using ID_Indetails.api.DTO.Response.Activity;
using ID_Indetails.api.DTO.Response.Common;

namespace ID_Indetails.api.Services.Activity.Abstraction;

public interface IActivityFeedService
{
    Task<PagedResponse<ActivityItemResponse>> ListAsync(ActivityListQuery query, CancellationToken cancellationToken = default);

    Task<ActivityTodayResponse> TodayAsync(CancellationToken cancellationToken = default);
}
