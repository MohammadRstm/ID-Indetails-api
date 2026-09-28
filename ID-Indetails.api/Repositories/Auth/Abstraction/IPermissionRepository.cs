using ID_Indetails.api.Business.Auth;

namespace ID_Indetails.api.Repositories.Auth.Abstraction;

public interface IPermissionRepository
{
    /// <summary>The permissions granted to a role, each with the domain it is limited to (if any).</summary>
    Task<IReadOnlyList<EffectivePermissions.Grant>> GetGrantsForRoleAsync(
        string roleCode,
        CancellationToken cancellationToken = default);
}
