using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Auth.Implementation;

public sealed class RefreshTokenRepository(IdIndetailsDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => db.RefreshTokens
            .AsNoTracking()
            .Include(t => t.User).ThenInclude(u => u.Team)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);

    public async Task<bool> TryRevokeAsync(long tokenId, DateTime revokedAt, CancellationToken cancellationToken = default)
        => await db.RefreshTokens
            .Where(t => t.Id == tokenId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt), cancellationToken) == 1;

    public Task SetReplacedByAsync(long tokenId, long replacedById, CancellationToken cancellationToken = default)
        => db.RefreshTokens
            .Where(t => t.Id == tokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplacedById, replacedById), cancellationToken);

    public Task RevokeAllActiveForUserAsync(long userId, DateTime revokedAt, CancellationToken cancellationToken = default)
        => db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt), cancellationToken);
}
