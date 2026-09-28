using ID_Indetails.api.Authorization.Requirements;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Services.Auth.Abstraction;
using Microsoft.AspNetCore.Authorization;

namespace ID_Indetails.api.Authorization.Handlers;

/// <summary>
/// Checks the permission against the user's current access in the database (role defaults
/// plus their personal overrides), looked up by user id. The role in the token is never
/// used, so grants, revokes and automatic role changes apply on the very next request.
/// </summary>
public sealed class PermissionHandler(IPermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (!long.TryParse(context.User.FindFirst(AuthClaimTypes.UserId)?.Value, out var userId))
        {
            return;
        }

        var access = await permissions.GetAccessAsync(userId);
        if (requirement.AnyOf.Any(access.Has))
        {
            context.Succeed(requirement);
        }
    }
}
