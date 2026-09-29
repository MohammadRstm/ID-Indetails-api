using ID_Indetails.api.Authorization;
using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Auth.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAuthService authService,
    IPasswordResetService passwordReset,
    ICurrentUser currentUser,
    RefreshTokenCookie refreshCookie) : ControllerBase
{
    /// <summary>Signs in. Returns the access token; the refresh token is set as an HttpOnly cookie.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
        => SignedIn(await authService.LoginAsync(request, cancellationToken));

    /// <summary>Exchanges the refresh cookie for a new access token and a new refresh cookie.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            return SignedIn(await authService.RefreshAsync(refreshCookie.Read(Request), cancellationToken));
        }
        catch
        {
            // A refresh that fails for any reason leaves no stale cookie behind. The global
            // exception handler resets response headers before writing the error, so the
            // cookie is cleared as that error response starts rather than here.
            Response.OnStarting(() =>
            {
                refreshCookie.Clear(Response);
                return Task.CompletedTask;
            });
            throw;
        }
    }

    /// <summary>Revokes the refresh token and clears the cookie. Works even with an expired access token.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(refreshCookie.Read(Request), cancellationToken);
        refreshCookie.Clear(Response);
        return NoContent();
    }

    /// <summary>Changes the password. The only action allowed while a temporary password is pending.</summary>
    [HttpPost("change-password")]
    [Authorize(Policy = AuthorizationPolicies.AllowPendingPasswordChange)]
    public async Task<ActionResult<AuthResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
        => SignedIn(await authService.ChangePasswordAsync(currentUser.Id, request, cancellationToken));

    /// <summary>
    /// Emails a link for choosing a new password to the active account with this email. Always answers 204, so it
    /// never reveals whether the email belongs to anyone.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await passwordReset.RequestAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Sets a new password from an emailed link and signs the account out everywhere; the person then signs in.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await passwordReset.ResetAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>The signed-in user and their permissions, for restoring the session in the client.</summary>
    [HttpGet("me")]
    [Authorize(Policy = AuthorizationPolicies.AllowPendingPasswordChange)]
    public Task<CurrentUserResponse> Me(CancellationToken cancellationToken)
        => authService.GetCurrentUserAsync(currentUser.Id, cancellationToken);

    private AuthResponse SignedIn(AuthSession session)
    {
        refreshCookie.Write(Response, session.RefreshCookieValue, session.RefreshExpiresAt);
        return session.Response;
    }
}
