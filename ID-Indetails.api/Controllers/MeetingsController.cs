using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Meetings;
using ID_Indetails.api.DTO.Response.Meetings;
using ID_Indetails.api.Services.Meetings.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Project meetings. Meetings of a domain the caller can't see read as 404.</summary>
[ApiController]
[Route("api")]
public sealed class MeetingsController(IMeetingService meetingService) : ControllerBase
{
    /// <summary>Meetings of every project the caller can see, earliest first.</summary>
    [HttpGet("meetings")]
    [HasPermission(PermissionCodes.ProjectView)]
    public Task<IReadOnlyList<MeetingResponse>> List([FromQuery] MeetingListQuery query, CancellationToken cancellationToken)
        => meetingService.ListAsync(query, cancellationToken);

    [HttpGet("projects/{projectId:long}/meetings")]
    [HasPermission(PermissionCodes.ProjectView)]
    public Task<IReadOnlyList<MeetingResponse>> ListForProject(long projectId, CancellationToken cancellationToken)
        => meetingService.ListForProjectAsync(projectId, cancellationToken);

    [HttpPost("projects/{projectId:long}/meetings")]
    [HasAnyPermission(PermissionCodes.MeetingCreate, PermissionCodes.MeetingManage)]
    public async Task<ActionResult<MeetingResponse>> Create(long projectId, SaveMeetingRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await meetingService.CreateAsync(projectId, request, cancellationToken));

    /// <summary>Moves, resizes or renames a meeting: any with meeting:manage, your own with meeting:create.</summary>
    [HttpPut("meetings/{id:long}")]
    [HasAnyPermission(PermissionCodes.MeetingCreate, PermissionCodes.MeetingManage)]
    public Task<MeetingResponse> Update(long id, SaveMeetingRequest request, CancellationToken cancellationToken)
        => meetingService.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("meetings/{id:long}")]
    [HasAnyPermission(PermissionCodes.MeetingCreate, PermissionCodes.MeetingManage)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await meetingService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
