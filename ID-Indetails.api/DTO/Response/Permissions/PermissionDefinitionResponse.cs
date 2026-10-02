namespace ID_Indetails.api.DTO.Response.Permissions;

/// <param name="Explanation">What it lets someone do, in a sentence or two.</param>
/// <param name="Domain">The domain the permission is limited to; null for either domain.</param>
/// <param name="DefaultFor">The roles that hold it by default.</param>
public sealed record PermissionDefinitionResponse(
    string Code,
    string Description,
    string Explanation,
    string? Domain,
    IReadOnlyList<string> DefaultFor);

/// <summary>Where one of the caller's permissions comes from.</summary>
public enum PermissionSource
{
    /// <summary>Their role gives it.</summary>
    Role,

    /// <summary>It was given to them on top of their role.</summary>
    Granted,

    /// <summary>Their role gives it, but it was taken away from them: they don't hold it.</summary>
    Removed,
}

public sealed record MyPermissionResponse(string Code, string Description, string Explanation, PermissionSource Source);
