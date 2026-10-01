using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using Microsoft.Extensions.Caching.Memory;

namespace ID_Indetails.api.Services.Auth.Implementation;

/// <summary>
/// Permissions are read from the database (not from the token), so every request is checked
/// against the current role and overrides.
/// <list type="bullet">
/// <item>The catalogue and role defaults change only by migration, so they are cached.</item>
/// <item>Each user's access is cached too, and dropped (<see cref="Forget"/>) whenever their
/// role, status or overrides change, so a change applies on their very next request.</item>
/// </list>
/// The cache lives in this process: running several API instances would need a shared cache
/// or a per-user version stamp.
/// </summary>
public sealed class PermissionService(
    IPermissionRepository permissions,
    IUserRepository users,
    IMemoryCache cache) : IPermissionService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private const string CatalogKey = "permissions:catalog";
    private const string GrantsKey = "permissions:grants";

    public async Task<UserAccess> GetAccessAsync(long userId, CancellationToken cancellationToken = default)
    {
        var key = AccessKey(userId);
        if (cache.TryGetValue(key, out UserAccess? cached) && cached is not null)
        {
            return cached;
        }

        var user = await users.GetRoleSnapshotAsync(userId, cancellationToken);
        var access = user is null
            ? UserAccess.None(userId, string.Empty, null)
            : user.Status == UserStatus.Deactivated
                ? UserAccess.None(userId, user.RoleCode, user.DomainCode)
                : await ResolveAsync(user.Id, user.RoleCode, user.DomainCode, cancellationToken);
        access = access with { SessionsRevokedAt = user?.SessionsRevokedAt, SessionsValidFrom = user?.SessionsValidFrom };

        cache.Set(key, access, CacheFor);
        return access;
    }

    public Task<UserAccess> ResolveAsync(User user, CancellationToken cancellationToken = default)
        => user.Status == UserStatus.Deactivated
            ? Task.FromResult(UserAccess.None(user.Id, user.RoleCode, user.DomainCode))
            : ResolveAsync(user.Id, user.RoleCode, user.DomainCode, cancellationToken);

    public async Task<IReadOnlyList<EffectivePermissions.Grant>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CatalogKey, out IReadOnlyList<EffectivePermissions.Grant>? cached) && cached is not null)
        {
            return cached;
        }

        var catalog = (await permissions.GetAllAsync(cancellationToken))
            .Select(p => new EffectivePermissions.Grant(p.Code, p.DomainCode))
            .ToList();

        cache.Set(CatalogKey, (IReadOnlyList<EffectivePermissions.Grant>)catalog, CacheFor);
        return catalog;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<EffectivePermissions.Grant>>> GetGrantsByRoleAsync(
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(GrantsKey, out IReadOnlyDictionary<string, IReadOnlyList<EffectivePermissions.Grant>>? cached)
            && cached is not null)
        {
            return cached;
        }

        var grants = await permissions.GetGrantsByRoleAsync(cancellationToken);
        cache.Set(GrantsKey, grants, CacheFor);
        return grants;
    }

    public void Forget(long userId) => cache.Remove(AccessKey(userId));

    private async Task<UserAccess> ResolveAsync(
        long userId,
        string roleCode,
        string? domainCode,
        CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogAsync(cancellationToken);
        var grants = await GetGrantsByRoleAsync(cancellationToken);
        var overrides = await permissions.GetOverridesAsync(userId, cancellationToken: cancellationToken);

        return EffectivePermissions.Resolve(
            userId,
            roleCode,
            domainCode,
            catalog,
            grants.TryGetValue(roleCode, out var roleGrants) ? roleGrants : [],
            overrides.Select(o => new EffectivePermissions.Override(o.PermissionCode, o.Effect)));
    }

    private static string AccessKey(long userId) => $"permissions:user:{userId}";
}
