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
}
