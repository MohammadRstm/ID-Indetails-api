using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Activity;
using ID_Indetails.api.DTO.Response.Activity;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Services.Activity.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>What happened in the domains the caller can see.</summary>
[ApiController]
[Route("api/activity")]
public sealed class ActivityController(IActivityFeedService activityFeedService) : ControllerBase
{
    /// <summary>Newest first.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.ActivityView)]
    public Task<PagedResponse<ActivityItemResponse>> List([FromQuery] ActivityListQuery query, CancellationToken cancellationToken)
        => activityFeedService.ListAsync(query, cancellationToken);

    /// <summary>Design work that started today.</summary>
    [HttpGet("today")]
    [HasPermission(PermissionCodes.ActivityView)]
    [HasPermission(PermissionCodes.WorkEntryViewAll)]
    public Task<ActivityTodayResponse> Today(CancellationToken cancellationToken)
        => activityFeedService.TodayAsync(cancellationToken);
}
