using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Auth.Abstraction;

/// <summary>
/// A signed-in session: the response body plus the refresh token, which the website's controller sets as the cookie and
/// the phone app's returns in the body.
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
    /// Starts a new website session: an access token and a new refresh token for an active user. The refresh token
    /// is staged for saving; the caller commits it with the unit of work.
    /// </summary>
    Task<AuthSession> StartAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="StartAsync(User, CancellationToken)"/>, for <paramref name="client"/>'s session
    /// <paramref name="sessionId"/>: a new one, or the next token of one being refreshed. A phone's token lasts
    /// <c>MobileSession:RefreshTokenDays</c> and its access token carries the session id.
    /// </summary>
    Task<AuthSession> StartAsync(User user, RefreshTokenClient client, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>The current user as returned to the client, with effective permissions.</summary>
    Task<CurrentUserResponse> DescribeAsync(User user, CancellationToken cancellationToken = default);
}
