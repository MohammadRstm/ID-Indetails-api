using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Portal.Implementation;

public sealed class PortalSessionRepository(IdIndetailsDbContext db) : IPortalSessionRepository
{
    public Task<PortalRefreshToken?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default)
        => db.PortalRefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public void Add(PortalRefreshToken token) => db.PortalRefreshTokens.Add(token);

    public async Task<bool> TryRevokeAsync(long tokenId, DateTime revokedAt, RevokeReason reason, CancellationToken cancellationToken = default)
        => await db.PortalRefreshTokens
            .Where(t => t.Id == tokenId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, revokedAt)
                .SetProperty(t => t.RevokeReason, reason), cancellationToken) == 1;

    public Task SetReplacedByAsync(long tokenId, long replacedById, CancellationToken cancellationToken = default)
        => db.PortalRefreshTokens
            .Where(t => t.Id == tokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplacedById, replacedById), cancellationToken);

    public Task RevokeAllForAccountAsync(long accountId, DateTime revokedAt, CancellationToken cancellationToken = default)
        => db.PortalRefreshTokens
            .Where(t => t.AccountId == accountId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, revokedAt)
                .SetProperty(t => t.RevokeReason, RevokeReason.Revoked), cancellationToken);

    public Task<PortalPasswordResetToken?> GetResetAsync(string tokenHash, CancellationToken cancellationToken = default)
        => db.PortalPasswordResetTokens.Include(r => r.Account).SingleOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);

    public void Add(PortalPasswordResetToken reset) => db.PortalPasswordResetTokens.Add(reset);

    public async Task<bool> TryUseResetAsync(long resetId, DateTime usedAt, CancellationToken cancellationToken = default)
        => await db.PortalPasswordResetTokens
            .Where(r => r.Id == resetId && r.UsedAt == null && r.ExpiresAt > usedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAt, usedAt), cancellationToken) == 1;

    public Task ExpirePendingResetsAsync(long accountId, DateTime now, CancellationToken cancellationToken = default)
        => db.PortalPasswordResetTokens
            .Where(r => r.AccountId == accountId && r.UsedAt == null && r.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, now), cancellationToken);
}
