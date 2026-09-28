using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Invitations.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Invitations.Implementation;

public sealed class InvitationRepository(IdIndetailsDbContext db) : IInvitationRepository
{
    public Task<UserInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => db.UserInvitations
            .Include(i => i.User).ThenInclude(u => u.Team)
            .SingleOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

    public void Add(UserInvitation invitation) => db.UserInvitations.Add(invitation);

    public async Task<bool> TryAcceptAsync(long invitationId, DateTime acceptedAt, CancellationToken cancellationToken = default)
        => await db.UserInvitations
            .Where(i => i.Id == invitationId
                && i.AcceptedAt == null
                && i.RevokedAt == null
                && i.ExpiresAt > acceptedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedAt, acceptedAt), cancellationToken) == 1;

    public Task RevokePendingForUserAsync(long userId, DateTime revokedAt, CancellationToken cancellationToken = default)
        => db.UserInvitations
            .Where(i => i.UserId == userId && i.AcceptedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, revokedAt), cancellationToken);
}
