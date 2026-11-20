using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Repositories.DevSignIn.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.DevSignIn.Implementation;

public sealed class DevAccountRepository(IdIndetailsDbContext db) : IDevAccountRepository
{
    public async Task<IReadOnlyList<DevStaffAccount>> SignableStaffAsync(CancellationToken cancellationToken = default)
        => await db.Users
            .AsNoTracking()
            .Where(u => u.Status == UserStatus.Active && u.SessionsRevokedAt == null)
            .Select(u => new DevStaffAccount(
                u.Id, u.Email, u.FirstName, u.LastName, u.RoleCode, u.DomainCode, u.Position, u.Team == null ? null : u.Team.Name))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DevPortalAccount>> ActivePortalAccountsAsync(CancellationToken cancellationToken = default)
        => await db.PortalAccounts
            .AsNoTracking()
            .Where(a => a.Status == PortalAccountStatus.Active)
            .Select(a => new DevPortalAccount(a.Id, a.Email, a.FirstName, a.LastName))
            .ToListAsync(cancellationToken);
}
