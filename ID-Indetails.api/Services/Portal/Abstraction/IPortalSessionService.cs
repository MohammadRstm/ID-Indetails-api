using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>A signed-in portal session: the body to return, and the refresh cookie to set.</summary>
public sealed record PortalAuthSession(
    PortalAuthResponse Response,
    string RefreshCookieValue,
    DateTime RefreshExpiresAt,
    PortalRefreshToken StoredRefreshToken);

public interface IPortalSessionService
{
    /// <summary>Stages a refresh token and issues a portal access token. Saved by the caller's unit of work.</summary>
    Task<PortalAuthSession> StartAsync(PortalAccount account, Guid sessionId, CancellationToken cancellationToken = default);

    Task<PortalAccountResponse> DescribeAsync(PortalAccount account, CancellationToken cancellationToken = default);
}
