using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>Staff managing which of a Design project's client contacts can follow it in the client portal.</summary>
[ApiController]
[Route("api/projects/{projectId:long}/portal")]
[HasPermission(PermissionCodes.ClientAccessManage)]
public sealed class ProjectPortalController(IPortalAccessService portalAccess) : ControllerBase
{
    /// <summary>The client's email contacts and where each stands: not invited, invited, expired, active or ended.</summary>
    [HttpGet("clients")]
    public Task<PortalClientsResponse> Clients(long projectId, CancellationToken cancellationToken)
        => portalAccess.ListAsync(projectId, cancellationToken);

    /// <summary>
    /// Invites one of the client's email contacts. A new address gets a one-time link valid for 24 hours to set a
    /// password; an address that already has an account gets access at once and an email saying so.
    /// </summary>
    [HttpPost("invitations")]
    [EnableRateLimiting(RateLimitPolicies.Email)]
    public Task<PortalClientsResponse> Invite(long projectId, InvitePortalClientRequest request, CancellationToken cancellationToken)
        => portalAccess.InviteAsync(projectId, request, cancellationToken);

    /// <summary>Sends a new link; the earlier one stops working.</summary>
    [HttpPost("invitations/{invitationId:long}/resend")]
    [EnableRateLimiting(RateLimitPolicies.Email)]
    public Task<PortalClientsResponse> Resend(long projectId, long invitationId, CancellationToken cancellationToken)
        => portalAccess.ResendAsync(projectId, invitationId, cancellationToken);

    [HttpPost("invitations/{invitationId:long}/revoke")]
    public Task<PortalClientsResponse> RevokeInvitation(long projectId, long invitationId, CancellationToken cancellationToken)
        => portalAccess.RevokeInvitationAsync(projectId, invitationId, cancellationToken);

    /// <summary>Ends the client's access to the project; they are signed out if it was their last project.</summary>
    [HttpPost("access/{accessId:long}/revoke")]
    public Task<PortalClientsResponse> RevokeAccess(long projectId, long accessId, CancellationToken cancellationToken)
        => portalAccess.RevokeAccessAsync(projectId, accessId, cancellationToken);
}
