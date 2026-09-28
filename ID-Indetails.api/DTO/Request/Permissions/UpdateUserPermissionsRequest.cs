using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Permissions;

/// <summary>Changes applied together, in order, then checked once for an automatic role change.</summary>
public sealed record UpdateUserPermissionsRequest
{
    public IReadOnlyList<PermissionChangeRequest> Changes { get; init; } = [];
}

public sealed record PermissionChangeRequest
{
    /// <summary>A permission code such as <c>invoice:void</c>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>grant, revoke, or reset (back to the role default).</summary>
    public PermissionChangeAction Action { get; init; }

    /// <summary>Optional note kept with the override and in the activity history.</summary>
    public string? Reason { get; init; }
}
