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

    public async Task<IReadOnlyList<PortalAccount>> GetByEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken = default)
        => await db.PortalAccounts.AsNoTracking().Where(a => emails.Contains(a.Email)).ToListAsync(cancellationToken);

    public void Add(PortalAccount account) => db.PortalAccounts.Add(account);
}
