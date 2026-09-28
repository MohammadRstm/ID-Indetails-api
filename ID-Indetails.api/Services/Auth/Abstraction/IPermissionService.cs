using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Auth.Abstraction;

/// <summary>Resolves what users may do (see <c>Business/Auth/EffectivePermissions</c>).</summary>
public interface IPermissionService
{
    /// <summary>
    /// The user's current access, read from the database (not the token) and cached briefly.
    /// A missing or deactivated account has no permissions.
    /// </summary>
    Task<UserAccess> GetAccessAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Access for a user already loaded, using their current role and domain. Not cached.</summary>
    Task<UserAccess> ResolveAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>Every permission that exists, with the domain it is limited to.</summary>
    Task<IReadOnlyList<EffectivePermissions.Grant>> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>The default permissions of every role.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<EffectivePermissions.Grant>>> GetGrantsByRoleAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Drops the cached access of a user whose role, status or overrides changed.</summary>
    void Forget(long userId);
}
