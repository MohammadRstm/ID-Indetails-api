using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Auth.Implementation;

public sealed class RefreshTokenRepository(IdIndetailsDbContext db) : IRefreshTokenRepository
{
    /// <summary>
    /// Superseded tokens only come from retries within the replay grace, so a walk is a few steps; this bounds a broken
    /// chain.
    /// </summary>
    private const int MaxSuccessors = 50;

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => db.RefreshTokens
            .AsNoTracking()
            .Include(t => t.User).ThenInclude(u => u.Team)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> SuccessorsAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        var successors = new List<RefreshToken>();
        var nextId = token.ReplacedById;
        while (nextId is { } id && successors.Count < MaxSuccessors)
        {
            var next = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (next is null)
            {
                break;
            }

            successors.Add(next);
            nextId = next.RevokeReason == RevokeReason.Superseded ? next.ReplacedById : null;
        }

        return successors;
    }

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);

    public async Task<bool> TryRevokeAsync(long tokenId, DateTime revokedAt, RevokeReason reason, CancellationToken cancellationToken = default)
        => await db.RefreshTokens
            .Where(t => t.Id == tokenId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, revokedAt)
                .SetProperty(t => t.RevokeReason, reason), cancellationToken) == 1;

    public Task SetReplacedByAsync(long tokenId, long replacedById, CancellationToken cancellationToken = default)
        => db.RefreshTokens
            .Where(t => t.Id == tokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplacedById, replacedById), cancellationToken);

    public Task RevokeAllActiveForUserAsync(long userId, DateTime revokedAt, CancellationToken cancellationToken = default)
        => db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, revokedAt)
                .SetProperty(t => t.RevokeReason, RevokeReason.Revoked), cancellationToken);

    public Task<bool> IsSessionLiveAsync(long userId, Guid sessionId, DateTime now, CancellationToken cancellationToken = default)
        => db.RefreshTokens.AnyAsync(
            t => t.UserId == userId && t.SessionId == sessionId && t.RevokedAt == null && t.ExpiresAt > now, cancellationToken);

    public Task RevokeSessionAsync(Guid sessionId, DateTime revokedAt, RevokeReason reason, CancellationToken cancellationToken = default)
        => db.RefreshTokens
            .Where(t => t.SessionId == sessionId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, revokedAt)
                .SetProperty(t => t.RevokeReason, reason), cancellationToken);
}
