using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Auth;

/// <summary>
/// Works out what a user holds, in this order:
/// <list type="number">
/// <item>Super admins hold every permission. Overrides never apply to them.</item>
/// <item>Everyone else starts from their role's defaults in their home domain (permissions
/// limited to the other domain are left out),</item>
/// <item>then gains their <see cref="PermissionEffect.Grant"/> overrides and loses their
/// <see cref="PermissionEffect.Revoke"/> overrides.</item>
/// </list>
/// Only employees log hours: admins and super admins never hold <see cref="PermissionCatalog.OwnWork"/>, whatever the
/// overrides say. Holding a permission of another domain opens that domain's data to the user. Someone whose home is a
/// service (the website) holds only that service's permissions by default.
/// </summary>
public static class EffectivePermissions
{
    /// <param name="DomainCode">The domain the permission is limited to; null for either domain.</param>
    public sealed record Grant(string Code, string? DomainCode);

    public sealed record Override(string Code, PermissionEffect Effect);

    /// <summary>The role's default permissions for someone whose home domain is <paramref name="userDomain"/>.</summary>
    public static IReadOnlySet<string> RoleDefaults(string roleCode, string? userDomain, IEnumerable<Grant> roleGrants)
    {
        var applicable = roleCode == RoleCodes.SuperAdmin
            ? roleGrants
            : roleGrants.Where(g => g.DomainCode == userDomain || (g.DomainCode is null && DomainCodes.IsBusiness(userDomain)));

        return applicable
            .Select(g => g.Code)
            .Where(code => roleCode == RoleCodes.Employee || !PermissionCatalog.OwnWork.Contains(code))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Role defaults with the overrides applied. Overrides for unknown permissions are ignored.</summary>
    public static IReadOnlySet<string> Apply(
        IReadOnlySet<string> defaults,
        IEnumerable<Override> overrides,
        IReadOnlyCollection<Grant> catalog)
    {
        var known = catalog.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
        var effective = new HashSet<string>(defaults, StringComparer.Ordinal);

        foreach (var o in overrides.Where(o => known.Contains(o.Code)))
        {
            if (o.Effect == PermissionEffect.Grant)
            {
                effective.Add(o.Code);
            }
            else
            {
                effective.Remove(o.Code);
            }
        }

        return effective;
    }

    /// <param name="catalog">Every permission that exists, with the domain it is limited to.</param>
    /// <param name="roleGrants">The grants of <paramref name="roleCode"/>.</param>
    public static UserAccess Resolve(
        long userId,
        string roleCode,
        string? userDomain,
        IReadOnlyCollection<Grant> catalog,
        IEnumerable<Grant> roleGrants,
        IEnumerable<Override> overrides)
    {
        if (roleCode == RoleCodes.SuperAdmin)
        {
            return new UserAccess(
                userId, roleCode, userDomain, Sorted(catalog.Select(p => p.Code).Except(PermissionCatalog.OwnWork)), IsJoker: false, DomainCodes.All);
        }

        var defaults = RoleDefaults(roleCode, userDomain, roleGrants);
        var effective = Apply(defaults, overrides, catalog);
        if (roleCode != RoleCodes.Employee)
        {
            effective = effective.Except(PermissionCatalog.OwnWork).ToHashSet(StringComparer.Ordinal);
        }

        return new UserAccess(
            userId,
            roleCode,
            userDomain,
            Sorted(effective),
            IsJoker: !effective.SetEquals(defaults),
            VisibleDomains(userDomain, effective, catalog));
    }

    /// <summary>The home domain plus every domain the user holds a domain-limited permission of.</summary>
    public static IReadOnlyList<string> VisibleDomains(
        string? userDomain,
        IReadOnlySet<string> effective,
        IEnumerable<Grant> catalog)
    {
        var domains = catalog
            .Where(p => p.DomainCode is not null && effective.Contains(p.Code))
            .Select(p => p.DomainCode!)
            .Append(userDomain)
            .OfType<string>()
            .ToHashSet();

        return DomainCodes.All.Where(domains.Contains).ToList();
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> codes)
        => codes.Distinct().Order(StringComparer.Ordinal).ToList();
}
