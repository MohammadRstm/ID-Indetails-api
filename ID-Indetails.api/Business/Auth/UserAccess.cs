using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Auth;

/// <summary>
/// What a user may do right now: their effective permissions and the domains whose data they
/// may see. Built by <see cref="EffectivePermissions.Resolve"/>; endpoints only ever ask for
/// permissions, never for a role.
/// </summary>
/// <param name="IsJoker">True when an admin or employee holds a different set from their role defaults.</param>
/// <param name="VisibleDomains">
/// Their home domain, plus the other domain once they hold one of its permissions. Super
/// admins see both.
/// </param>
public sealed record UserAccess(
    long UserId,
    string Role,
    string? Domain,
    IReadOnlyList<string> Permissions,
    bool IsJoker,
    IReadOnlyList<string> VisibleDomains)
{
    public bool IsSuperAdmin => Role == RoleCodes.SuperAdmin;

    public bool Has(string permission) => Permissions.Contains(permission);

    /// <summary>
    /// Whether records of <paramref name="domain"/> are visible. Company-level records
    /// (<c>null</c>) are visible to everyone; what they may do with them is up to permissions.
    /// </summary>
    public bool CanSee(string? domain) => domain is null || VisibleDomains.Contains(domain);

    /// <summary>A deactivated or missing account: nothing at all.</summary>
    public static UserAccess None(long userId, string role, string? domain) => new(userId, role, domain, [], false, []);
}
