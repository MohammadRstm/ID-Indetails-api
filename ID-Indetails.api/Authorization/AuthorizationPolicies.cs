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

    /// <summary>Separates the alternatives in a "permission:a|b" policy name.</summary>
    public const char AnyOfSeparator = '|';

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
/// Lets in anyone holding at least one of the permissions, for endpoints whose finer rules
/// are decided in the service: <c>[HasAnyPermission(PermissionCodes.A, PermissionCodes.B)]</c>.
/// </summary>
public sealed class HasAnyPermissionAttribute(params string[] permissions)
    : AuthorizeAttribute(AuthorizationPolicies.PermissionPrefix + string.Join(AuthorizationPolicies.AnyOfSeparator, permissions));

/// <summary>
/// Builds "permission:xyz" (and "permission:a|b") policies on demand: signed in, password
/// change done, permission held. Each permission does not need its own registration.
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

        var anyOf = policyName[AuthorizationPolicies.PermissionPrefix.Length..]
            .Split(AuthorizationPolicies.AnyOfSeparator, StringSplitOptions.RemoveEmptyEntries);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PasswordChangeCompletedRequirement(), new PermissionRequirement(anyOf))
            .Build();
    }
}
