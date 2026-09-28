using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Auth.Abstraction;

public interface IRefreshTokenRepository
{
    /// <summary>Untracked, with the user and their team loaded.</summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Stages a new token; saved by the unit of work.</summary>
    void Add(RefreshToken token);

    /// <summary>
    /// Revokes the token only if it is still active, in a single statement. Returns false when
    /// another request already used it, which is how concurrent reuse is detected.
    /// </summary>
    Task<bool> TryRevokeAsync(long tokenId, DateTime revokedAt, CancellationToken cancellationToken = default);

    Task SetReplacedByAsync(long tokenId, long replacedById, CancellationToken cancellationToken = default);

    /// <summary>Signs the user out everywhere.</summary>
    Task RevokeAllActiveForUserAsync(long userId, DateTime revokedAt, CancellationToken cancellationToken = default);
}
