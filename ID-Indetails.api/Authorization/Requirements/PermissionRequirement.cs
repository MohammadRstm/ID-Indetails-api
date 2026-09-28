using Microsoft.AspNetCore.Authorization;

namespace ID_Indetails.api.Authorization.Requirements;

/// <summary>The signed-in user must currently hold at least one of <see cref="AnyOf"/>.</summary>
public sealed class PermissionRequirement(IReadOnlyList<string> anyOf) : IAuthorizationRequirement
{
    public IReadOnlyList<string> AnyOf { get; } = anyOf;
}
