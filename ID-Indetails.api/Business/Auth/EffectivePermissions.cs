using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Auth;

/// <summary>
/// The permissions a user actually holds: their role's grants, minus permissions that
/// belong to the other domain. Super admins hold every granted permission in both domains.
/// </summary>
public static class EffectivePermissions
{
    public sealed record Grant(string Code, string? DomainCode);

    public static IReadOnlyList<string> For(string roleCode, string? userDomain, IEnumerable<Grant> roleGrants)
    {
        var applicable = roleCode == RoleCodes.SuperAdmin
            ? roleGrants
            : roleGrants.Where(g => g.DomainCode is null || g.DomainCode == userDomain);

        return applicable.Select(g => g.Code).Distinct().Order(StringComparer.Ordinal).ToList();
    }
}
