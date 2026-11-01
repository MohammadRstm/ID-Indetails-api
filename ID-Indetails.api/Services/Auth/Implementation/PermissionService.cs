using System.Collections.Concurrent;
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
    IMemoryCache cache,
    PermissionLoads loads) : IPermissionService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private const string CatalogKey = "permissions:catalog";
    private const string GrantsKey = "permissions:grants";

    public async Task<UserAccess> GetAccessAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(AccessKey(userId), out UserAccess? cached) && cached is not null)
        {
            return cached;
        }

        // A page opening sends many requests at once: the first loads the access, the others wait for it.
        Lazy<Task<UserAccess>>? mine = null;
        mine = new Lazy<Task<UserAccess>>(() => LoadAccessAsync(userId, mine!));
        var load = loads.Running.GetOrAdd(userId, mine);

        // The loader waits it out even if its caller leaves: the load runs on its request's database connection.
        return load == mine ? await load.Value : await load.Value.WaitAsync(cancellationToken);
    }

    private async Task<UserAccess> LoadAccessAsync(long userId, Lazy<Task<UserAccess>> self)
    {
        try
        {
            var user = await users.GetRoleSnapshotAsync(userId, CancellationToken.None);
            var access = user is null
                ? UserAccess.None(userId, string.Empty, null)
                : user.Status == UserStatus.Deactivated
                    ? UserAccess.None(userId, user.RoleCode, user.DomainCode)
                    : await ResolveAsync(user.Id, user.RoleCode, user.DomainCode, CancellationToken.None);
            access = access with { SessionsRevokedAt = user?.SessionsRevokedAt, SessionsValidFrom = user?.SessionsValidFrom };

            // a change meanwhile (Forget) took this load off the list: what it read may be out of date, so it isn't kept
            if (loads.Running.TryRemove(KeyValuePair.Create(userId, self)))
            {
                cache.Set(AccessKey(userId), access, CacheFor);
            }

            return access;
        }
        catch
        {
            loads.Running.TryRemove(KeyValuePair.Create(userId, self));
            throw;
        }
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

    public void Forget(long userId)
    {
        cache.Remove(AccessKey(userId));
        loads.Running.TryRemove(userId, out _);
    }

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

/// <summary>The access loads under way, by user, shared by every request (a singleton beside the cache).</summary>
public sealed class PermissionLoads
{
    public ConcurrentDictionary<long, Lazy<Task<UserAccess>>> Running { get; } = new();
}
