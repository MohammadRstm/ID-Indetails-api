using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Data;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Auth.Implementation;

public sealed class PermissionRepository(IdIndetailsDbContext db) : IPermissionRepository
{
    public async Task<IReadOnlyList<EffectivePermissions.Grant>> GetGrantsForRoleAsync(
        string roleCode,
        CancellationToken cancellationToken = default)
        => await db.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.RoleCode == roleCode)
            .Select(rp => new EffectivePermissions.Grant(rp.Permission.Code, rp.Permission.DomainCode))
            .ToListAsync(cancellationToken);
}
