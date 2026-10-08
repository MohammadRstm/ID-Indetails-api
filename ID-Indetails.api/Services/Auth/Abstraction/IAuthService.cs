using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.DTO.Response.Auth;

namespace ID_Indetails.api.Services.Auth.Abstraction;

public interface IAuthService
{
    Task<AuthSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates the refresh token: the presented one is revoked and replaced. Presenting a token
    /// that was already rotated signs the user out everywhere (it may have been stolen).
    /// </summary>
    Task<AuthSession> RefreshAsync(string? refreshCookieValue, CancellationToken cancellationToken = default);

    /// <summary>Revokes the presented refresh token. Always succeeds.</summary>
    Task LogoutAsync(string? refreshCookieValue, CancellationToken cancellationToken = default);

    /// <summary>Changes the password, clears must-change-password and signs out every other session.</summary>
    Task<AuthSession> ChangePasswordAsync(long userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);

    Task<CurrentUserResponse> GetCurrentUserAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Signs in on the phone: a new mobile session, with the same checks as <see cref="LoginAsync"/>.</summary>
    Task<AuthSession> MobileLoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates a phone's refresh token by the mobile rules (<c>Business/Auth/MobileRefreshRules</c>): a token presented
    /// again because its answer was lost gets a new one within the grace; later it ends that session; a token held by
    /// two parties ends every session.
    /// </summary>
    Task<AuthSession> MobileRefreshAsync(string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Ends the phone's session the token belongs to and removes its push device. Always succeeds.</summary>
    Task MobileLogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="ChangePasswordAsync"/>, keeping the phone's session <paramref name="sessionId"/> (a new one when
    /// null) with a new refresh token.
    /// </summary>
    Task<AuthSession> MobileChangePasswordAsync(
        long userId,
        Guid? sessionId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}
