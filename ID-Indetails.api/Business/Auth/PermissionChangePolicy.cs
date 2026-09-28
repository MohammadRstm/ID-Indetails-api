using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Auth;

/// <summary>
/// Who may change whose permissions. The caller already holds <c>permission:manage</c>
/// (checked by the endpoint); on top of that:
/// <list type="bullet">
/// <item>A super admin's permissions can never be changed.</item>
/// <item>Nobody changes their own permissions.</item>
/// <item>Deactivated accounts cannot be changed. Invited ones can, so access is ready when they sign up.</item>
/// <item>Anyone but a super admin may only grant, revoke or reset permissions they hold
/// themselves, so nobody can hand out more than they have.</item>
/// </list>
/// Whether the caller can see the target at all (their domain) is checked before this; a
/// target in a domain the caller cannot see reads as not found.
/// </summary>
public static class PermissionChangePolicy
{
    public enum DenialKind
    {
        Forbidden,
        BusinessRule,
    }

    public sealed record Denial(DenialKind Kind, string Code, string Message);

    /// <returns>Null when the change is allowed.</returns>
    public static Denial? Check(
        UserAccess actor,
        long targetId,
        string targetRole,
        UserStatus targetStatus,
        IEnumerable<string> codes)
    {
        if (targetRole == RoleCodes.SuperAdmin)
        {
            return new Denial(DenialKind.Forbidden, ErrorCodes.PermissionsLocked,
                "A super admin always holds every permission; theirs cannot be changed.");
        }

        if (targetId == actor.UserId)
        {
            return new Denial(DenialKind.Forbidden, ErrorCodes.PermissionChangeNotAllowed,
                "You cannot change your own permissions.");
        }

        if (targetStatus == UserStatus.Deactivated)
        {
            return new Denial(DenialKind.BusinessRule, ErrorCodes.UserDeactivated,
                "This account is deactivated; its permissions cannot be changed.");
        }

        if (!actor.IsSuperAdmin)
        {
            var notHeld = codes.Where(c => !actor.Has(c)).Distinct().Order(StringComparer.Ordinal).ToList();
            if (notHeld.Count > 0)
            {
                return new Denial(DenialKind.Forbidden, ErrorCodes.PermissionChangeNotAllowed,
                    $"You can only change permissions you hold yourself. You do not hold: {string.Join(", ", notHeld)}.");
            }
        }

        return null;
    }
}
