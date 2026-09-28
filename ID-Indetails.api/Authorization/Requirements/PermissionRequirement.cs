using Microsoft.AspNetCore.Authorization;

namespace ID_Indetails.api.Authorization.Requirements;

/// <summary>The signed-in user's role must currently hold <see cref="Permission"/> in their domain.</summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
