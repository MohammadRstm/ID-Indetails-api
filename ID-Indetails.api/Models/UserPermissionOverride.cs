using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A personal exception to a user's role defaults: a permission added or taken away. Only
/// differences from the role defaults are stored, so a user with any row is a "joker".
/// Super admins never have rows: they always hold every permission.
/// </summary>
public class UserPermissionOverride
{
    public long UserId { get; set; }

    public string PermissionCode { get; set; } = string.Empty;

    public PermissionEffect Effect { get; set; }

    /// <summary>Why it was changed, as typed by whoever changed it.</summary>
    public string? Reason { get; set; }

    /// <summary>Who last set this override.</summary>
    public long GrantedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public User User { get; set; } = null!;

    public Permission Permission { get; set; } = null!;
}
