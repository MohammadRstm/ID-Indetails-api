using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Invitations;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Invitations;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Invitations.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

[ApiController]
[Route("api/invitations")]
public sealed class InvitationsController(
    IInvitationService invitationService,
    RefreshTokenCookie refreshCookie) : ControllerBase
{
    /// <summary>
    /// Invites someone by email: admins and employees need <c>employee:invite</c> (into a domain
    /// the inviter works in), super admins need <c>employee:invite_super_admin</c>.
    /// </summary>
    [HttpPost]
    [HasAnyPermission(PermissionCodes.EmployeeInvite, PermissionCodes.EmployeeInviteSuperAdmin)]
    public async Task<ActionResult<InvitationResponse>> Create(CreateInvitationRequest request, CancellationToken cancellationToken)
    {
        var invitation = await invitationService.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, invitation);
    }

    /// <summary>Emails a fresh signup link; the previous link stops working.</summary>
    [HttpPost("{userId:long}/resend")]
    [HasAnyPermission(PermissionCodes.EmployeeInvite, PermissionCodes.EmployeeInviteSuperAdmin)]
    public Task<InvitationResponse> Resend(long userId, CancellationToken cancellationToken)
        => invitationService.ResendAsync(userId, cancellationToken);

    /// <summary>For the signup page: who the link is for, before they fill in their details.</summary>
    [HttpPost("validate")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public Task<InvitationPreviewResponse> Validate(InvitationTokenRequest request, CancellationToken cancellationToken)
        => invitationService.PreviewAsync(request, cancellationToken);

    /// <summary>Completes signup and signs the new user in (sets the refresh cookie).</summary>
    [HttpPost("accept")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> Accept(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        var session = await invitationService.AcceptAsync(request, cancellationToken);
        refreshCookie.Write(Response, session.RefreshCookieValue, session.RefreshExpiresAt);
        return session.Response;
    }
}
