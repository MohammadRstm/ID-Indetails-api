using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.TimeOff;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.TimeOff;
using ID_Indetails.api.Services.TimeOff.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Deciding time off (super admins), and who is off (everyone signed in, for the Off flags).</summary>
[ApiController]
[Route("api/time-off")]
public sealed class TimeOffController(ITimeOffService timeOff) : ControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.TimeOffDecide)]
    public Task<PagedResponse<TimeOffResponse>> List([FromQuery] TimeOffListQuery query, CancellationToken cancellationToken)
        => timeOff.ListAsync(query, cancellationToken);

    [HttpPost("{id:long}/approve")]
    [HasPermission(PermissionCodes.TimeOffDecide)]
    public Task<TimeOffResponse> Approve(long id, ApproveTimeOffRequest request, CancellationToken cancellationToken)
        => timeOff.ApproveAsync(id, request, cancellationToken);

    [HttpPost("{id:long}/decline")]
    [HasPermission(PermissionCodes.TimeOffDecide)]
    public Task<TimeOffResponse> Decline(long id, TimeOffNoteRequest request, CancellationToken cancellationToken)
        => timeOff.DeclineAsync(id, request, cancellationToken);

    [HttpPost("{id:long}/reject")]
    [HasPermission(PermissionCodes.TimeOffDecide)]
    public Task<TimeOffResponse> Reject(long id, TimeOffNoteRequest request, CancellationToken cancellationToken)
        => timeOff.RejectAsync(id, request, cancellationToken);

    /// <summary>Who is off and until when: never why, nor whether it is paid.</summary>
    [HttpGet("away")]
    [Authorize]
    public Task<IReadOnlyList<AwayResponse>> Away([FromQuery] AwayQuery query, CancellationToken cancellationToken)
        => timeOff.AwayAsync(query, cancellationToken);
}
