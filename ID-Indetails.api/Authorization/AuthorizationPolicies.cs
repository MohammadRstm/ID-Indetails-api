using ID_Indetails.api.Authorization.Requirements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Authorization;

public static class AuthorizationPolicies
{
    /// <summary>Signed in, even with a generated password still pending replacement.</summary>
    public const string AllowPendingPasswordChange = "AllowPendingPasswordChange";

    /// <summary>Prefix of the dynamic permission policies built by <see cref="PermissionPolicyProvider"/>.</summary>
    public const string PermissionPrefix = "permission:";

    /// <summary>The default for <c>[Authorize]</c>: signed in and not stuck on a generated password.</summary>
    public static AuthorizationPolicy Default { get; } = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new PasswordChangeCompletedRequirement())
        .Build();
}

/// <summary>Use on an action or controller: <c>[HasPermission(PermissionCodes.EmployeeInvite)]</c>.</summary>
public sealed class HasPermissionAttribute(string permission)
    : AuthorizeAttribute(AuthorizationPolicies.PermissionPrefix + permission);

/// <summary>
/// Builds "permission:xyz" policies on demand (signed in, password change done, permission
/// held), so each permission does not need its own registration.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(AuthorizationPolicies.PermissionPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        var permission = policyName[AuthorizationPolicies.PermissionPrefix.Length..];
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PasswordChangeCompletedRequirement(), new PermissionRequirement(permission))
            .Build();
    }
}
