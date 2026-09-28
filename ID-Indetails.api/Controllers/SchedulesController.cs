using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Services.Company.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Personal working weeks. People outside the caller's domains read as 404.</summary>
[ApiController]
[Route("api")]
[HasPermission(PermissionCodes.ScheduleManage)]
public sealed class SchedulesController(IScheduleService schedules) : ControllerBase
{
    /// <summary>Everyone's week (their own or the company's) with the hours it requires.</summary>
    [HttpGet("company/schedules")]
    public Task<SchedulesResponse> List([FromQuery] ScheduleListQuery query, CancellationToken cancellationToken)
        => schedules.ListAsync(query, cancellationToken);

    /// <summary>A person's own week, or back to the company week, from today or a later date.</summary>
    [HttpPut("employees/{userId:long}/schedule")]
    public Task<PersonScheduleResponse> Set(long userId, SetScheduleRequest request, CancellationToken cancellationToken)
        => schedules.SetAsync(userId, request, cancellationToken);
}
