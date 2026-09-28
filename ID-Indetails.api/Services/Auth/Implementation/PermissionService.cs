using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using Microsoft.Extensions.Caching.Memory;

namespace ID_Indetails.api.Services.Auth.Implementation;

/// <summary>
/// Permissions are read from the database (not from the token), so every request is checked
/// against the current grants. They change only by migration, so a short cache is safe.
/// </summary>
public sealed class PermissionService(IPermissionRepository permissions, IMemoryCache cache) : IPermissionService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<string>> GetEffectiveAsync(
        string roleCode,
        string? domainCode,
        CancellationToken cancellationToken = default)
    {
        var key = $"permissions:{roleCode}:{domainCode ?? "-"}";
        if (cache.TryGetValue(key, out IReadOnlyList<string>? cached) && cached is not null)
        {
            return cached;
        }

        var grants = await permissions.GetGrantsForRoleAsync(roleCode, cancellationToken);
        var effective = EffectivePermissions.For(roleCode, domainCode, grants);

        cache.Set(key, effective, CacheFor);
        return effective;
    }
}
