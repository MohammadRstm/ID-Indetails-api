using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Teams;
using ID_Indetails.api.DTO.Response.Teams;
using ID_Indetails.api.Services.Teams.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Teams (departments) of each domain. Teams of a domain the caller can't see read as 404.</summary>
[ApiController]
[Route("api/teams")]
public sealed class TeamsController(ITeamService teamService) : ControllerBase
{
    /// <summary>Teams of the caller's visible domains, for pickers and filters.</summary>
    [HttpGet]
    [Authorize]
    public Task<IReadOnlyList<TeamResponse>> List(CancellationToken cancellationToken)
        => teamService.ListAsync(cancellationToken);

    [HttpPost]
    [HasPermission(PermissionCodes.TeamManage)]
    public async Task<ActionResult<TeamResponse>> Create(CreateTeamRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await teamService.CreateAsync(request, cancellationToken));

    [HttpPut("{teamId:long}")]
    [HasPermission(PermissionCodes.TeamManage)]
    public Task<TeamResponse> Rename(long teamId, RenameTeamRequest request, CancellationToken cancellationToken)
        => teamService.RenameAsync(teamId, request, cancellationToken);

    /// <summary>Only while nobody is in the team.</summary>
    [HttpDelete("{teamId:long}")]
    [HasPermission(PermissionCodes.TeamManage)]
    public async Task<IActionResult> Delete(long teamId, CancellationToken cancellationToken)
    {
        await teamService.DeleteAsync(teamId, cancellationToken);
        return NoContent();
    }
}
