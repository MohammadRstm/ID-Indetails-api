using ID_Indetails.api.Authorization;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>A client opening their invitation link: what it is for, then setting up the account with their own password.</summary>
[ApiController]
[Route("api/portal/invitations")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class PortalInvitationsController(IPortalAuthService portalAuth, PortalRefreshCookie refreshCookie) : ControllerBase
{
    /// <summary>The email and project the link is for, or why it can't be used: invalid, expired, cancelled or already used.</summary>
    [HttpPost("validate")]
    public Task<PortalInvitationPreviewResponse> Validate(PortalInvitationTokenRequest request, CancellationToken cancellationToken)
        => portalAuth.PreviewInvitationAsync(request, cancellationToken);

    /// <summary>Sets the client's name and password, gives access to the project and signs them in.</summary>
    [HttpPost("accept")]
    public async Task<ActionResult<PortalAuthResponse>> Accept(AcceptPortalInvitationRequest request, CancellationToken cancellationToken)
    {
        var session = await portalAuth.AcceptInvitationAsync(request, cancellationToken);
        refreshCookie.Write(Response, session.RefreshCookieValue, session.RefreshExpiresAt);
        return session.Response;
    }
}
