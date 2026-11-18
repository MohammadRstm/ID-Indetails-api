using ID_Indetails.api.Authorization;
using ID_Indetails.api.DTO.Request.DevSignIn;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.DevSignIn;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Services.DevSignIn.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Development only: the sign-in page's account picker signs in as any active account, without its password.
/// Outside the Development environment this controller isn't part of the app at all (see <c>AddDevSignIn</c>): its
/// addresses answer like ones that don't exist. It is never in the OpenAPI document either.
/// </summary>
[ApiController]
[Route("api/dev")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class DevSignInController(
    IDevSignInService devSignIn,
    RefreshTokenCookie refreshCookie,
    PortalRefreshCookie portalRefreshCookie) : ControllerBase
{
    /// <summary>The accounts one can sign in as, in groups: super admins, each domain's admins and employees, clients.</summary>
    [HttpGet("accounts")]
    public Task<DevAccountsResponse> Accounts(CancellationToken cancellationToken)
        => devSignIn.ListAsync(cancellationToken);

    /// <summary>Signs in as a staff member, as a right password would: the refresh token is set as the cookie.</summary>
    [HttpPost("sign-in")]
    public async Task<ActionResult<AuthResponse>> SignIn(DevSignInRequest request, CancellationToken cancellationToken)
    {
        var session = await devSignIn.SignInAsync(request.UserId, cancellationToken);
        refreshCookie.Write(Response, session.RefreshCookieValue, session.RefreshExpiresAt);
        return session.Response;
    }

    /// <summary>Signs in to the client portal as a client, with the portal's own refresh cookie.</summary>
    [HttpPost("portal-sign-in")]
    public async Task<ActionResult<PortalAuthResponse>> PortalSignIn(DevPortalSignInRequest request, CancellationToken cancellationToken)
    {
        var session = await devSignIn.PortalSignInAsync(request.AccountId, cancellationToken);
        portalRefreshCookie.Write(Response, session.RefreshCookieValue, session.RefreshExpiresAt);
        return session.Response;
    }
}
