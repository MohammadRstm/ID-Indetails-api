using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Auth;

/// <summary>
/// Turns requested permission changes into the user's new role and overrides.
/// <list type="bullet">
/// <item>Only differences from the role defaults are kept as overrides, so a user whose
/// permissions match their role defaults has none (and is not a joker).</item>
/// <item>When the resulting permissions are exactly the defaults of the other role (admin or
/// employee) in the user's domain, the user takes that role at once, and the overrides are
/// cleared because the new role's defaults now describe them.</item>
/// <item>Logging one's own hours (<see cref="PermissionCatalog.OwnWork"/>) comes with the employee role: an admin is
/// never given it, and it doesn't count when matching the other role's defaults.</item>
/// </list>
/// Super admins are never planned for: they cannot be changed, and nobody is ever moved into
/// or out of the super admin role automatically.
/// </summary>
public static class PermissionChangePlanner
{
    public sealed record Change(string Code, PermissionChangeAction Action);

    /// <param name="Applied">The requested changes that actually changed what the user holds.</param>
    /// <param name="PreviousRole">Set when the user's role changed automatically.</param>
    public sealed record Plan(
        string Role,
        IReadOnlyDictionary<string, PermissionEffect> Overrides,
        IReadOnlyList<Change> Applied,
        string? PreviousRole);

    /// <param name="grantsByRole">The grants of each role.</param>
    public static Plan Build(
        string roleCode,
        string? userDomain,
        IReadOnlyCollection<EffectivePermissions.Grant> catalog,
        IReadOnlyDictionary<string, IReadOnlyList<EffectivePermissions.Grant>> grantsByRole,
        IEnumerable<EffectivePermissions.Override> currentOverrides,
        IEnumerable<Change> changes)
    {
        if (roleCode is not (RoleCodes.Admin or RoleCodes.Employee))
        {
            throw new InvalidOperationException("Only the permissions of admins and employees can be changed.");
        }

        var defaults = DefaultsOf(roleCode, userDomain, grantsByRole);
        var effective = new HashSet<string>(
            EffectivePermissions.Apply(defaults, currentOverrides, catalog), StringComparer.Ordinal);
        var isAdmin = roleCode == RoleCodes.Admin;
        if (isAdmin)
        {
            effective.ExceptWith(PermissionCatalog.OwnWork);
        }

        var applied = new List<Change>();
        foreach (var change in changes.Where(c => !(isAdmin && PermissionCatalog.OwnWork.Contains(c.Code))))
        {
            var changed = change.Action switch
            {
                PermissionChangeAction.Grant => effective.Add(change.Code),
                PermissionChangeAction.Revoke => effective.Remove(change.Code),
                _ => defaults.Contains(change.Code) ? effective.Add(change.Code) : effective.Remove(change.Code),
            };

            if (changed)
            {
                applied.Add(change);
            }
        }

        var otherRole = isAdmin ? RoleCodes.Employee : RoleCodes.Admin;
        if (WithoutOwnWork(effective).SetEquals(WithoutOwnWork(DefaultsOf(otherRole, userDomain, grantsByRole))))
        {
            return new Plan(otherRole, new Dictionary<string, PermissionEffect>(), applied, roleCode);
        }

        var overrides = new Dictionary<string, PermissionEffect>(StringComparer.Ordinal);
        foreach (var code in effective.Except(defaults))
        {
            overrides[code] = PermissionEffect.Grant;
        }

        foreach (var code in defaults.Except(effective))
        {
            overrides[code] = PermissionEffect.Revoke;
        }

        return new Plan(roleCode, overrides, applied, null);
    }

    private static HashSet<string> WithoutOwnWork(IEnumerable<string> codes)
        => codes.Except(PermissionCatalog.OwnWork).ToHashSet(StringComparer.Ordinal);

    private static IReadOnlySet<string> DefaultsOf(
        string roleCode,
        string? userDomain,
        IReadOnlyDictionary<string, IReadOnlyList<EffectivePermissions.Grant>> grantsByRole)
        => EffectivePermissions.RoleDefaults(
            roleCode, userDomain, grantsByRole.TryGetValue(roleCode, out var grants) ? grants : []);
}
