using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Portal.Implementation;

public sealed class PortalInvitationRepository(IdIndetailsDbContext db) : IPortalInvitationRepository
{
    public Task<PortalInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => db.PortalInvitations
            .Include(i => i.Account)
            .Include(i => i.Project)
            .SingleOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

    public Task<PortalInvitation?> GetAsync(long invitationId, CancellationToken cancellationToken = default)
        => db.PortalInvitations.Include(i => i.Account).SingleOrDefaultAsync(i => i.Id == invitationId, cancellationToken);

    public async Task<IReadOnlyList<PortalInvitation>> LatestForProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var all = await db.PortalInvitations.AsNoTracking()
            .Where(i => i.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        return all.GroupBy(i => i.AccountId).Select(g => g.MaxBy(i => i.Id)!).ToList();
    }

    public async Task<IReadOnlyList<PortalInvitation>> PendingForAccountAsync(long accountId, DateTime now, CancellationToken cancellationToken = default)
        => await db.PortalInvitations
            .Include(i => i.Project)
            .Where(i => i.AccountId == accountId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .ToListAsync(cancellationToken);

    public Task<bool> HasPendingAsync(long accountId, long projectId, DateTime now, CancellationToken cancellationToken = default)
        => db.PortalInvitations.AnyAsync(
            i => i.AccountId == accountId && i.ProjectId == projectId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now,
            cancellationToken);

    public void Add(PortalInvitation invitation) => db.PortalInvitations.Add(invitation);

    public async Task<bool> TryAcceptAsync(long invitationId, DateTime acceptedAt, CancellationToken cancellationToken = default)
        => await db.PortalInvitations
            .Where(i => i.Id == invitationId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > acceptedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedAt, acceptedAt), cancellationToken) == 1;

    public Task RevokePendingAsync(long accountId, long projectId, DateTime revokedAt, CancellationToken cancellationToken = default)
        => db.PortalInvitations
            .Where(i => i.AccountId == accountId && i.ProjectId == projectId && i.AcceptedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, revokedAt), cancellationToken);

    public Task RevokePendingForProjectAsync(long projectId, DateTime revokedAt, CancellationToken cancellationToken = default)
        => db.PortalInvitations
            .Where(i => i.ProjectId == projectId && i.AcceptedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, revokedAt), cancellationToken);
}
