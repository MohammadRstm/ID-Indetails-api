namespace ID_Indetails.api.Models;

public class RolePermission
{
    public string RoleCode { get; set; } = string.Empty;

    public string PermissionCode { get; set; } = string.Empty;

    public Role Role { get; set; } = null!;

    public Permission Permission { get; set; } = null!;
}
