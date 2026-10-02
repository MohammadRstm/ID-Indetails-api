using ID_Indetails.api.DTO.Request.Permissions;
using ID_Indetails.api.DTO.Response.Permissions;

namespace ID_Indetails.api.Services.Permissions.Abstraction;

/// <summary>
/// Viewing and changing individual people's permissions (callers hold <c>permission:manage</c>), and everyone's view of
/// their own.
/// </summary>
public interface IPermissionManagementService
{
    /// <summary>What the caller holds, and what their role gives but was taken away from them, with what each allows.</summary>
    Task<IReadOnlyList<MyPermissionResponse>> GetMineAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionDefinitionResponse>> GetCatalogAsync(CancellationToken cancellationToken = default);

    Task<UserPermissionsResponse> GetForUserAsync(long userId, CancellationToken cancellationToken = default);

    Task<UserPermissionsResponse> UpdateAsync(
        long userId,
        UpdateUserPermissionsRequest request,
        CancellationToken cancellationToken = default);
}
