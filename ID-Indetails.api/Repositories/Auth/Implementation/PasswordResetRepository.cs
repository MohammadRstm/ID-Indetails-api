using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Auth.Implementation;

public sealed class PasswordResetRepository(IdIndetailsDbContext db) : IPasswordResetRepository
{
    public Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => db.PasswordResetTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public void Add(PasswordResetToken token) => db.PasswordResetTokens.Add(token);

    public async Task<bool> TryUseAsync(long tokenId, DateTime usedAt, CancellationToken cancellationToken = default)
        => await db.PasswordResetTokens
            .Where(t => t.Id == tokenId && t.UsedAt == null && t.ExpiresAt > usedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, usedAt), cancellationToken) == 1;

    public Task ExpirePendingForUserAsync(long userId, DateTime now, CancellationToken cancellationToken = default)
        => db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, now), cancellationToken);
}
