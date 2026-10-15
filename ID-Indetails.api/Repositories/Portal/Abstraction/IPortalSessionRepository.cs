using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Portal.Abstraction;

/// <summary>Portal refresh tokens and password-reset links.</summary>
public interface IPortalSessionRepository
{
    /// <summary>Untracked.</summary>
    Task<PortalRefreshToken?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default);

    void Add(PortalRefreshToken token);

    /// <summary>Revokes the token only if it is still active, in a single statement: of two refreshes at once, one wins.</summary>
    Task<bool> TryRevokeAsync(long tokenId, DateTime revokedAt, RevokeReason reason, CancellationToken cancellationToken = default);

    Task SetReplacedByAsync(long tokenId, long replacedById, CancellationToken cancellationToken = default);

    /// <summary>Signs the account out everywhere.</summary>
    Task RevokeAllForAccountAsync(long accountId, DateTime revokedAt, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with the account loaded.</summary>
    Task<PortalPasswordResetToken?> GetResetAsync(string tokenHash, CancellationToken cancellationToken = default);

    void Add(PortalPasswordResetToken reset);

    /// <summary>Marks the link used only if it is unused and unexpired, in a single statement.</summary>
    Task<bool> TryUseResetAsync(long resetId, DateTime usedAt, CancellationToken cancellationToken = default);

    Task ExpirePendingResetsAsync(long accountId, DateTime now, CancellationToken cancellationToken = default);
}
