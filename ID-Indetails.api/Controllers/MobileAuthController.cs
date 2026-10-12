using ID_Indetails.api.Authorization;
using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Auth.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Sign-in for the phone app. The same checks as the website's, but the refresh token travels in the body, lasts 30
/// days since last use and has its own replay rules (docs/authentication.md, "Mobile sessions").
/// </summary>
[ApiController]
[Route("api/mobile/auth")]
public sealed class MobileAuthController(
    IAuthService authService,
    ICurrentUser currentUser,
    ILoggerFactory loggers) : ControllerBase
{
    /// <summary>Signs in and starts a new session for this phone.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<MobileAuthResponse> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Issued(await authService.MobileLoginAsync(request, cancellationToken));
        }
        catch (UnauthenticatedException)
        {
            // Who tried is the address only: the email typed may not be anyone's.
            loggers.CreateLogger(SecurityLog.Category).LogWarning("Mobile sign-in refused for {Caller}.", SecurityLog.Caller(HttpContext));
            throw;
        }
    }

    /// <summary>Exchanges the refresh token for a new access token and a new refresh token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<MobileAuthResponse> Refresh(MobileRefreshTokenRequest request, CancellationToken cancellationToken)
        => Issued(await authService.MobileRefreshAsync(request.RefreshToken, cancellationToken));

    /// <summary>Ends this phone's session and stops its pushes. 204 also when the token is unknown.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(MobileRefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await authService.MobileLogoutAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    /// <summary>Changes the password. Every other session ends; this phone gets a new refresh token.</summary>
    [HttpPost("change-password")]
    [Authorize(Policy = AuthorizationPolicies.AllowPendingPasswordChange)]
    [EnableRateLimiting(RateLimitPolicies.Credential)]
    public async Task<MobileAuthResponse> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
        => Issued(await authService.MobileChangePasswordAsync(currentUser.Id, currentUser.SessionId, request, cancellationToken));

    private static MobileAuthResponse Issued(AuthSession session) => new()
    {
        AccessToken = session.Response.AccessToken,
        AccessTokenExpiresAt = session.Response.AccessTokenExpiresAt,
        RefreshToken = session.RefreshCookieValue,
        RefreshTokenExpiresAt = session.RefreshExpiresAt,
        User = session.Response.User,
    };
}
