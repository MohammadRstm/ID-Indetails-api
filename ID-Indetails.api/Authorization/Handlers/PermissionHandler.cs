using ID_Indetails.api.Authorization.Requirements;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Services.Auth.Abstraction;
using Microsoft.AspNetCore.Authorization;

namespace ID_Indetails.api.Authorization.Handlers;

/// <summary>
/// Checks the permission against the database grants for the user's role and domain, so a
/// change to role permissions applies immediately rather than when the token expires.
/// </summary>
public sealed class PermissionHandler(IPermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var role = context.User.FindFirst(AuthClaimTypes.Role)?.Value;
        if (role is null)
        {
            return;
        }

        var domain = context.User.FindFirst(AuthClaimTypes.Domain)?.Value;
        var effective = await permissions.GetEffectiveAsync(role, domain);

        if (effective.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
