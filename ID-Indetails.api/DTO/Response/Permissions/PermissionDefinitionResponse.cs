namespace ID_Indetails.api.DTO.Response.Permissions;

/// <param name="Domain">The domain the permission is limited to; null for either domain.</param>
/// <param name="DefaultFor">The roles that hold it by default.</param>
public sealed record PermissionDefinitionResponse(
    string Code,
    string Description,
    string? Domain,
    IReadOnlyList<string> DefaultFor);
