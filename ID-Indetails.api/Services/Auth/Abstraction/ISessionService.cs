using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Auth.Abstraction;

/// <summary>
/// A signed-in session: the response body plus the refresh cookie the controller must set.
/// <see cref="StoredRefreshToken"/> is the staged database row (its id is known once saved);
/// it is for services only and is never returned to the client.
/// </summary>
public sealed record AuthSession(
    AuthResponse Response,
    string RefreshCookieValue,
    DateTime RefreshExpiresAt,
    RefreshToken StoredRefreshToken);

public interface ISessionService
{
    /// <summary>
    /// Issues an access token and a new refresh token for an active user. The refresh token
    /// is staged for saving; the caller commits it with the unit of work.
    /// </summary>
    Task<AuthSession> StartAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>The current user as returned to the client, with effective permissions.</summary>
    Task<CurrentUserResponse> DescribeAsync(User user, CancellationToken cancellationToken = default);
}
