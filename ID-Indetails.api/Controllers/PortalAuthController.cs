using ID_Indetails.api.Authorization;
using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// The client portal's sign-in. Its refresh cookie is scoped to these endpoints only, apart from the staff one, so a
/// client and a staff member can be signed in in the same browser.
/// </summary>
[ApiController]
[Route("api/portal/auth")]
public sealed class PortalAuthController(
    IPortalAuthService portalAuth,
    PortalRefreshCookie refreshCookie,
    ILoggerFactory loggers) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<PortalAuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return SignedIn(await portalAuth.LoginAsync(request, cancellationToken));
        }
        catch (UnauthenticatedException)
        {
            loggers.CreateLogger(SecurityLog.Category).LogWarning("Portal sign-in refused for {Caller}.", SecurityLog.Caller(HttpContext));
            throw;
        }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<ActionResult<PortalAuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            return SignedIn(await portalAuth.RefreshAsync(refreshCookie.Read(Request), cancellationToken));
        }
        catch (UnauthenticatedException)
        {
            // A refused refresh leaves no stale cookie behind (see the staff AuthController).
            Response.OnStarting(() =>
            {
                refreshCookie.Clear(Response);
                return Task.CompletedTask;
            });
            throw;
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await portalAuth.LogoutAsync(refreshCookie.Read(Request), cancellationToken);
        refreshCookie.Clear(Response);
        return NoContent();
    }

    /// <summary>Emails a reset link to the active portal account with this email. Always 204: it never says whether the email has one.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await portalAuth.RequestPasswordResetAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Sets a new password from an emailed link and signs the account out everywhere.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await portalAuth.ResetPasswordAsync(request, cancellationToken);
        return NoContent();
    }

    private PortalAuthResponse SignedIn(PortalAuthSession session)
    {
        refreshCookie.Write(Response, session.RefreshCookieValue, session.RefreshExpiresAt);
        return session.Response;
    }
}
