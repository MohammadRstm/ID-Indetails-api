using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Auth.Abstraction;

public interface IPermissionRepository
{
    /// <summary>Every permission that exists, not tracked.</summary>
    Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>The default permissions of every role, each with the domain it is limited to (if any).</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<EffectivePermissions.Grant>>> GetGrantsByRoleAsync(
        CancellationToken cancellationToken = default);

    /// <summary>A user's permission overrides. Tracked when <paramref name="forUpdate"/> is true.</summary>
    Task<IReadOnlyList<UserPermissionOverride>> GetOverridesAsync(
        long userId,
        bool forUpdate = false,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new override; saved by the unit of work.</summary>
    void AddOverride(UserPermissionOverride permissionOverride);

    /// <summary>Stages removing an override; saved by the unit of work.</summary>
    void RemoveOverride(UserPermissionOverride permissionOverride);
}
