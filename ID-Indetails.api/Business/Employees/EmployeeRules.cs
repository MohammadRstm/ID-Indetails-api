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
            ? DomainCodes.All.All(viewer.VisibleDomains.Contains)
            : viewer.CanSee(targetDomain);

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
    /// Ending all of someone's sessions, which also blocks their sign-in until it is restored: a super admin's to do,
    /// to someone else's working account.
    /// </summary>
    public static RuleDenial? CheckEndSessions(UserAccess actor, Target target, bool alreadyEnded)
    {
        if (CheckSessionsActor(actor, target) is { } denial)
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

    /// <summary>Letting someone whose sessions were ended sign in again: a super admin's to do.</summary>
    public static RuleDenial? CheckRestoreSignIn(UserAccess actor, Target target, bool ended)
        => CheckSessionsActor(actor, target)
            ?? (ended ? null : RuleDenial.BusinessRule(ErrorCodes.SessionsNotEnded, "Their sign-in isn't suspended."));

    private static RuleDenial? CheckSessionsActor(UserAccess actor, Target target)
    {
        if (!actor.IsSuperAdmin)
        {
            return RuleDenial.Forbidden(ErrorCodes.EmployeeChangeNotAllowed, "Only a super admin can end or restore someone's sessions.");
        }

        return target.Id == actor.UserId
            ? RuleDenial.Forbidden(ErrorCodes.EmployeeChangeNotAllowed, "You cannot end or restore your own sessions.")
            : null;
    }

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

            return actor.CanSee(requestedDomain)
                ? (new RoleDecision(newRole, requestedDomain), null)
                : Deny(RuleDenial.Forbidden(ErrorCodes.EmployeeChangeNotAllowed, "You can only place people in a domain you work in."));
        }

        return requestedDomain is null || requestedDomain == target.Domain
            ? (new RoleDecision(newRole, target.Domain), null)
            : Deny(RuleDenial.Invalid(
                ErrorCodes.ValidationFailed, "domainCode", "Changing the role keeps the domain. Move the person to the other domain separately."));
    }

    /// <summary>Moving someone to the other domain. Only callers who see both domains can.</summary>
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

        if (!DomainCodes.All.All(actor.VisibleDomains.Contains))
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
