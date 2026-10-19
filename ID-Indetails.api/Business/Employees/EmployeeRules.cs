using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Employees;

/// <summary>
/// Who may see and change whose account. The endpoint has already checked the permission the
/// operation needs (<c>employee:view</c>, <c>employee:manage</c> or <c>employee:pay_manage</c>);
/// these are the rules that depend on the person being changed:
/// <list type="bullet">
/// <item>People in a domain the caller cannot see read as not found. Super admins belong to no
/// domain and are visible only to callers who see both domains.</item>
/// <item>Changing a super admin's account also needs <c>employee:invite_super_admin</c>.</item>
/// <item>Nobody deactivates, re-roles, moves or sets the pay of their own account.</item>
/// <item>The last active super admin can never be deactivated or given another role, so the
/// company is never left without one.</item>
/// </list>
/// </summary>
public static class EmployeeRules
{
    public sealed record Target(long Id, string Role, string? Domain, UserStatus Status);

    public sealed record RoleDecision(string Role, string? Domain);

    /// <summary>Whether <paramref name="viewer"/> may see a person whose home domain is <paramref name="targetDomain"/>.</summary>
    public static bool CanSee(UserAccess viewer, string? targetDomain)
        => targetDomain is null
            ? viewer.SeesEveryBusiness
            : viewer.CanSee(targetDomain);

    /// <summary>The website's staff are admins: it has no employees.</summary>
    public static RuleDenial? CheckRoleFitsDomain(string role, string? domain)
        => domain == DomainCodes.Website && role != RoleCodes.Admin
            ? RuleDenial.BusinessRule(ErrorCodes.NotForThisDomain, "Website staff are admins.")
            : null;

    /// <summary>Any change to someone's account (profile, pay, status, role, domain).</summary>
    public static RuleDenial? CheckCanChange(UserAccess actor, Target target)
        => target.Role == RoleCodes.SuperAdmin && !actor.Has(PermissionCodes.EmployeeInviteSuperAdmin)
            ? RuleDenial.Forbidden(
                ErrorCodes.EmployeeChangeNotAllowed,
                "Changing a super admin's account needs the permission to invite super admins.")
            : null;

    public static RuleDenial? CheckDeactivate(UserAccess actor, Target target, int activeSuperAdmins)
    {
        if (target.Id == actor.UserId)
        {
            return RuleDenial.Forbidden(ErrorCodes.EmployeeChangeNotAllowed, "You cannot deactivate your own account.");
        }

        if (CheckCanChange(actor, target) is { } denial)
        {
            return denial;
        }

        if (target.Status == UserStatus.Deactivated)
        {
            return RuleDenial.BusinessRule(ErrorCodes.UserAlreadyDeactivated, "This account is already deactivated.");
        }

        return IsLastSuperAdmin(target, activeSuperAdmins)
            ? RuleDenial.BusinessRule(ErrorCodes.LastSuperAdmin, "The last active super admin cannot be deactivated.")
            : null;
    }

    /// <summary>
    /// Ending all of someone's sessions, which also blocks their sign-in until it is restored (<c>session:manage</c>):
    /// someone else's working account.
    /// </summary>
    public static RuleDenial? CheckEndSessions(UserAccess actor, Target target, bool alreadyEnded)
    {
        if (CheckSessionsTarget(actor, target) is { } denial)
        {
            return denial;
        }

        if (target.Status != UserStatus.Active)
        {
            return RuleDenial.BusinessRule(ErrorCodes.UserDeactivated, "Only a working account has sessions to end.");
        }

        return alreadyEnded
            ? RuleDenial.BusinessRule(ErrorCodes.SessionsAlreadyEnded, "Their sessions are already ended.")
            : null;
    }

    /// <summary>Letting someone whose sessions were ended sign in again (<c>session:manage</c>).</summary>
    public static RuleDenial? CheckRestoreSignIn(UserAccess actor, Target target, bool ended)
        => CheckSessionsTarget(actor, target)
            ?? (ended ? null : RuleDenial.BusinessRule(ErrorCodes.SessionsNotEnded, "Their sign-in isn't suspended."));

    private static RuleDenial? CheckSessionsTarget(UserAccess actor, Target target)
        => target.Id == actor.UserId
            ? RuleDenial.Forbidden(ErrorCodes.EmployeeChangeNotAllowed, "You cannot end or restore your own sessions.")
            : CheckCanChange(actor, target);

    public static RuleDenial? CheckReactivate(UserAccess actor, Target target)
    {
        if (CheckCanChange(actor, target) is { } denial)
        {
            return denial;
        }

        return target.Status != UserStatus.Deactivated
            ? RuleDenial.BusinessRule(ErrorCodes.UserNotDeactivated, "This account is not deactivated.")
            : null;
    }

    /// <summary>
    /// A reactivated account is active again if the person had already signed up, otherwise
    /// it goes back to invited (and needs a fresh invitation link).
    /// </summary>
    public static UserStatus ReactivatedStatus(bool hasPassword) => hasPassword ? UserStatus.Active : UserStatus.Invited;

    /// <summary>
    /// A manual role change.
    /// <list type="bullet">
    /// <item>Becoming a super admin removes the domain (and team).</item>
    /// <item>A super admin becoming an admin or employee must be given a domain the caller can see.</item>
    /// <item>Between admin and employee the domain stays; moving domains is a separate operation.</item>
    /// <item>Website staff stay admins.</item>
    /// </list>
    /// </summary>
    public static (RoleDecision? Decision, RuleDenial? Denial) CheckRoleChange(
        UserAccess actor,
        Target target,
        string newRole,
        string? requestedDomain,
        int activeSuperAdmins)
    {
        if (target.Id == actor.UserId)
        {
            return Deny(RuleDenial.Forbidden(ErrorCodes.EmployeeChangeNotAllowed, "You cannot change your own role."));
        }

        if (target.Status == UserStatus.Deactivated)
        {
            return Deny(RuleDenial.BusinessRule(ErrorCodes.UserDeactivated, "This account is deactivated; reactivate it first."));
        }

        if (newRole == target.Role)
        {
            return Deny(RuleDenial.BusinessRule(ErrorCodes.RoleUnchanged, "This person already has that role."));
        }

        if ((newRole == RoleCodes.SuperAdmin || target.Role == RoleCodes.SuperAdmin)
            && !actor.Has(PermissionCodes.EmployeeInviteSuperAdmin))
        {
            return Deny(RuleDenial.Forbidden(
                ErrorCodes.EmployeeChangeNotAllowed,
                "Making or unmaking a super admin needs the permission to invite super admins."));
        }

        if (newRole == RoleCodes.SuperAdmin)
        {
            return requestedDomain is null
                ? (new RoleDecision(newRole, null), null)
                : Deny(RuleDenial.Invalid(ErrorCodes.SuperAdminHasNoDomain, "domainCode", "Super admins do not belong to a domain."));
        }

        if (target.Role == RoleCodes.SuperAdmin)
        {
            if (IsLastSuperAdmin(target, activeSuperAdmins))
            {
                return Deny(RuleDenial.BusinessRule(ErrorCodes.LastSuperAdmin, "The last active super admin must stay a super admin."));
            }

            if (requestedDomain is null)
            {
                return Deny(RuleDenial.Invalid(ErrorCodes.ValidationFailed, "domainCode", "Choose the domain the person will belong to."));
            }

            if (CheckRoleFitsDomain(newRole, requestedDomain) is { } unfit)
            {
                return Deny(unfit);
            }

            return actor.CanSee(requestedDomain)
                ? (new RoleDecision(newRole, requestedDomain), null)
                : Deny(RuleDenial.Forbidden(ErrorCodes.EmployeeChangeNotAllowed, "You can only place people in a domain you work in."));
        }

        if (requestedDomain is not null && requestedDomain != target.Domain)
        {
            return Deny(RuleDenial.Invalid(
                ErrorCodes.ValidationFailed, "domainCode", "Changing the role keeps the domain. Move the person to the other domain separately."));
        }

        return CheckRoleFitsDomain(newRole, target.Domain) is { } denial
            ? Deny(denial)
            : (new RoleDecision(newRole, target.Domain), null);
    }

    /// <summary>
    /// A role change, or a move to the other domain, hands the person the defaults of their (new) role in their (new)
    /// domain and drops their personal permission changes. Nobody but a super admin hands out a default they don't hold
    /// themselves, and a permission taken away by hand only comes back this way for someone who may manage permissions.
    /// </summary>
    /// <param name="revoked">The person's permissions taken away by hand.</param>
    public static RuleDenial? CheckRoleHandOut(UserAccess actor, IReadOnlyCollection<string> newDefaults, IEnumerable<string> revoked)
    {
        if (PermissionChangePolicy.NotHeld(actor, newDefaults) is { Count: > 0 } notHeld)
        {
            return RuleDenial.Forbidden(ErrorCodes.PermissionChangeNotAllowed,
                $"You can only give someone a role whose permissions you hold yourself. You do not hold: {string.Join(", ", notHeld)}.");
        }

        return !actor.IsSuperAdmin && !actor.Has(PermissionCodes.PermissionManage) && revoked.Any(newDefaults.Contains)
            ? RuleDenial.Forbidden(ErrorCodes.PermissionChangeNotAllowed,
                "Some of their permissions were taken away by hand, and this would give them back, which needs the permission to manage permissions.")
            : null;
    }

    /// <summary>Moving someone to the other business. Only callers who see both businesses can; website staff aren't moved.</summary>
    public static RuleDenial? CheckMove(UserAccess actor, Target target, string newDomain)
    {
        if (target.Id == actor.UserId)
        {
            return RuleDenial.Forbidden(ErrorCodes.EmployeeChangeNotAllowed, "You cannot move your own account.");
        }

        if (target.Role == RoleCodes.SuperAdmin)
        {
            return RuleDenial.BusinessRule(ErrorCodes.SuperAdminHasNoDomain, "Super admins do not belong to a domain.");
        }

        if (!DomainCodes.IsBusiness(target.Domain) || !DomainCodes.IsBusiness(newDomain))
        {
            return RuleDenial.BusinessRule(ErrorCodes.NotForThisDomain, "People are only moved between Design and Execution.");
        }

        if (!actor.SeesEveryBusiness)
        {
            return RuleDenial.Forbidden(
                ErrorCodes.EmployeeChangeNotAllowed, "Only someone who works in both domains can move people between them.");
        }

        if (target.Status == UserStatus.Deactivated)
        {
            return RuleDenial.BusinessRule(ErrorCodes.UserDeactivated, "This account is deactivated; reactivate it first.");
        }

        return newDomain == target.Domain
            ? RuleDenial.BusinessRule(ErrorCodes.DomainUnchanged, "This person is already in that domain.")
            : null;
    }

    /// <summary>
    /// Changing someone's sign-in address (<c>employee:change_email</c>), for an account that isn't deactivated. One's
    /// own goes through <c>POST /api/auth/change-email</c>, with their password.
    /// </summary>
    public static RuleDenial? CheckEmailChange(UserAccess actor, Target target)
    {
        if (target.Id == actor.UserId)
        {
            return RuleDenial.Forbidden(ErrorCodes.EmployeeChangeNotAllowed, "Change your own sign-in address from your profile, with your password.");
        }

        if (CheckCanChange(actor, target) is { } denial)
        {
            return denial;
        }

        return target.Status == UserStatus.Deactivated
            ? RuleDenial.BusinessRule(ErrorCodes.UserDeactivated, "This account is deactivated; reactivate it before changing its sign-in address.")
            : null;
    }

    /// <summary>Setting someone's pay: never your own, never a deactivated account.</summary>
    public static RuleDenial? CheckPayChange(UserAccess actor, Target target)
    {
        if (target.Id == actor.UserId)
        {
            return RuleDenial.Forbidden(ErrorCodes.EmployeeChangeNotAllowed, "You cannot set your own pay.");
        }

        if (CheckCanChange(actor, target) is { } denial)
        {
            return denial;
        }

        return target.Status == UserStatus.Deactivated
            ? RuleDenial.BusinessRule(ErrorCodes.UserDeactivated, "This account is deactivated; its pay cannot be changed.")
            : null;
    }

    private static bool IsLastSuperAdmin(Target target, int activeSuperAdmins)
        => target.Role == RoleCodes.SuperAdmin && target.Status == UserStatus.Active && activeSuperAdmins <= 1;

    private static (RoleDecision?, RuleDenial?) Deny(RuleDenial denial) => (null, denial);
}
