using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Permissions;

/// <summary>A user's permissions, and where each comes from.</summary>
public sealed record UserPermissionsResponse
{
    public required long UserId { get; init; }

    /// <summary>The user's current role (it may have just changed automatically).</summary>
    public required string Role { get; init; }

    /// <summary>Home domain; null for super admins.</summary>
    public string? Domain { get; init; }

    /// <summary>True for super admins: they always hold every permission and cannot be changed.</summary>
    public required bool IsLocked { get; init; }

    /// <summary>True when the user's permissions differ from their role defaults.</summary>
    public required bool IsJoker { get; init; }

    public required IReadOnlyList<string> VisibleDomains { get; init; }

    /// <summary>What the role gives in the user's home domain.</summary>
    public required IReadOnlyList<string> RoleDefaults { get; init; }

    /// <summary>What the user actually holds: role defaults plus overrides.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }

    public required IReadOnlyList<PermissionOverrideResponse> Overrides { get; init; }

    /// <summary>Set after an update that changed the user's role automatically.</summary>
    public string? PreviousRole { get; init; }
}

public sealed record PermissionOverrideResponse(
    string Code,
    PermissionEffect Effect,
    string? Reason,
    long GrantedById,
    DateTime SetAt);
