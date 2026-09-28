using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Permissions;
using ID_Indetails.api.DTO.Response.Permissions;
using ID_Indetails.api.Services.Permissions.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Individual people's permissions. Everything here needs <c>permission:manage</c>.</summary>
[ApiController]
[Route("api")]
[HasPermission(PermissionCodes.PermissionManage)]
public sealed class PermissionsController(IPermissionManagementService permissionService) : ControllerBase
{
    /// <summary>Every permission, what it allows, and which roles hold it by default.</summary>
    [HttpGet("permissions")]
    public Task<IReadOnlyList<PermissionDefinitionResponse>> GetCatalog(CancellationToken cancellationToken)
        => permissionService.GetCatalogAsync(cancellationToken);

    /// <summary>A user's role defaults, overrides and effective permissions.</summary>
    [HttpGet("users/{userId:long}/permissions")]
    public Task<UserPermissionsResponse> GetForUser(long userId, CancellationToken cancellationToken)
        => permissionService.GetForUserAsync(userId, cancellationToken);

    /// <summary>
    /// Grants, revokes or resets permissions for a user in one go. If the result exactly matches
    /// the other role's defaults, the user takes that role (see <c>PreviousRole</c>).
    /// </summary>
    [HttpPut("users/{userId:long}/permissions")]
    public Task<UserPermissionsResponse> Update(
        long userId,
        UpdateUserPermissionsRequest request,
        CancellationToken cancellationToken)
        => permissionService.UpdateAsync(userId, request, cancellationToken);
}
