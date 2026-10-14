using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Portal.Implementation;

public sealed class PortalAccountRepository(IdIndetailsDbContext db) : IPortalAccountRepository
{
    public Task<PortalAccountSessionState?> GetSessionStateAsync(long accountId, CancellationToken cancellationToken = default)
        => db.PortalAccounts
            .Where(a => a.Id == accountId)
            .Select(a => new PortalAccountSessionState(a.Status, a.SessionsValidFrom))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<PortalAccount?> GetByIdAsync(long accountId, CancellationToken cancellationToken = default)
        => db.PortalAccounts.SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken);

    public Task<PortalAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => db.PortalAccounts.SingleOrDefaultAsync(a => a.Email == email, cancellationToken);

    public async Task<PortalAccount?> GetByEmailForUpdateAsync(string email, CancellationToken cancellationToken = default)
        => (await db.PortalAccounts
            .FromSql($"SELECT * FROM `portal_accounts` WHERE `email` = {email} FOR UPDATE")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

    public Task LockAsync(long accountId, CancellationToken cancellationToken = default)
        => db.Database.ExecuteSqlAsync($"SELECT `id` FROM `portal_accounts` WHERE `id` = {accountId} FOR UPDATE", cancellationToken);

    public async Task<IReadOnlyList<PortalAccount>> GetByEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken = default)
        => await db.PortalAccounts.AsNoTracking().Where(a => emails.Contains(a.Email)).ToListAsync(cancellationToken);

    public void Add(PortalAccount account) => db.PortalAccounts.Add(account);

    public async Task<bool> CountWrongPasswordAsync(long accountId, int attempts, DateTime pausedUntil, CancellationToken cancellationToken = default)
    {
        await db.PortalAccounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.FailedSignIns, a => a.FailedSignIns + 1), cancellationToken);

        // Decided on the count in the database: of several wrong passwords at once, only the one that finds it at the
        // limit pauses sign-in.
        return await db.PortalAccounts
            .Where(a => a.Id == accountId && a.FailedSignIns >= attempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.FailedSignIns, 0)
                .SetProperty(a => a.LockedUntil, pausedUntil), cancellationToken) == 1;
    }
}
