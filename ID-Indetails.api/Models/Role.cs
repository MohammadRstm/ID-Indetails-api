namespace ID_Indetails.api.Models;

/// <summary>An access level (super_admin, admin, employee). Reference data.</summary>
public class Role
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Higher rank means more access; used to limit which roles someone may grant.</summary>
    public byte Rank { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
