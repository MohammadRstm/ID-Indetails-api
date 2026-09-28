using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Auth.Implementation;

public sealed class PermissionRepository(IdIndetailsDbContext db) : IPermissionRepository
{
    public async Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken cancellationToken = default)
        => await db.Permissions.AsNoTracking().OrderBy(p => p.Code).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<EffectivePermissions.Grant>>> GetGrantsByRoleAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await db.RolePermissions
            .AsNoTracking()
            .Select(rp => new { rp.RoleCode, rp.Permission.Code, rp.Permission.DomainCode })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.RoleCode)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<EffectivePermissions.Grant>)g
                    .Select(r => new EffectivePermissions.Grant(r.Code, r.DomainCode))
                    .ToList());
    }

    public async Task<IReadOnlyList<UserPermissionOverride>> GetOverridesAsync(
        long userId,
        bool forUpdate = false,
        CancellationToken cancellationToken = default)
    {
        var query = db.UserPermissionOverrides.Where(o => o.UserId == userId);
        if (!forUpdate)
        {
            query = query.AsNoTracking();
        }

        return await query.OrderBy(o => o.PermissionCode).ToListAsync(cancellationToken);
    }

    public void AddOverride(UserPermissionOverride permissionOverride) => db.UserPermissionOverrides.Add(permissionOverride);

    public void RemoveOverride(UserPermissionOverride permissionOverride) => db.UserPermissionOverrides.Remove(permissionOverride);
}
