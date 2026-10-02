namespace ID_Indetails.api.DTO.Response.Permissions;

/// <param name="Domain">The domain the permission is limited to; null for either domain.</param>
/// <param name="DefaultFor">The roles that hold it by default.</param>
public sealed record PermissionDefinitionResponse(
    string Code,
    string Description,
    string? Domain,
    IReadOnlyList<string> DefaultFor);

/// <summary>A permission the caller holds.</summary>
/// <param name="FromRole">False when it was given to them on top of their role.</param>
public sealed record HeldPermissionResponse(string Code, string Description, bool FromRole);
