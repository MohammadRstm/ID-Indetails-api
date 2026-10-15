using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Portal;

/// <summary>What an invitation link is worth when a client opens it.</summary>
public enum PortalInvitationState
{
    Usable,
    Expired,
    Revoked,

    /// <summary>Already used to set up the account: the client signs in instead.</summary>
    Used,
}

/// <summary>The client portal's rules: which projects can have one, who can be invited, links, and the sign-in lockout.</summary>
public static class PortalRules
{
    /// <summary>An invitation's state. An account that already set its password has used its link.</summary>
    public static PortalInvitationState StateOf(
        DateTime? acceptedAt, DateTime? revokedAt, DateTime expiresAt, PortalAccountStatus accountStatus, DateTime now)
    {
        if (acceptedAt is not null || accountStatus == PortalAccountStatus.Active)
        {
            return PortalInvitationState.Used;
        }

        if (revokedAt is not null)
        {
            return PortalInvitationState.Revoked;
        }

        return expiresAt <= now ? PortalInvitationState.Expired : PortalInvitationState.Usable;
    }

    /// <summary>
    /// Whether clients can be invited to the project: a Design client project with a client that isn't archived, and not
    /// archived itself (an archived project is read-only). Execution projects have no portal: callers answer 404 first.
    /// </summary>
    public static RuleDenial? CheckInvitable(PortalProjectFacts project)
    {
        if (project.BillingType != BillingType.Client || project.ClientId is null)
        {
            return RuleDenial.BusinessRule(ErrorCodes.PortalProjectNotEligible, "Only client projects have a client portal.");
        }

        if (project.Status == ProjectStatus.Archived)
        {
            return RuleDenial.BusinessRule(ErrorCodes.PortalProjectNotEligible, "This project is archived. Reopen it to invite its client.");
        }

        return project.ClientArchived
            ? RuleDenial.BusinessRule(ErrorCodes.PortalProjectNotEligible, "This project's client is archived. Restore the client to invite its contacts.")
            : null;
    }

    /// <summary>Only an email address among the project client's contacts can be invited. Both sides are normalised.</summary>
    public static bool IsClientContact(IReadOnlyList<PortalClientContact> contacts, string email)
        => contacts.Any(c => c.Email == email);

    /// <summary>
    /// Whether an invitation to the project can still be accepted: what inviting checked, checked again since any of it
    /// may have changed. A Design client project that isn't archived, of a client that isn't archived, that still has
    /// the address among its contacts. (Moving the project to another client cancels its invitations.)
    /// </summary>
    public static bool CanAccept(PortalProjectFacts project, string email)
        => project.DomainCode == DomainCodes.Design && CheckInvitable(project) is null && IsClientContact(project.EmailContacts, email);

    public static bool IsLocked(DateTime? lockedUntil, DateTime now) => lockedUntil > now;

    /// <summary>Where one email contact stands on the project, for the staff list.</summary>
    public static PortalClientStatus StatusOf(
        PortalAccountStatus? accountStatus,
        DateTime? accessRevokedAt,
        bool hasAccess,
        DateTime? invitationAcceptedAt,
        DateTime? invitationRevokedAt,
        DateTime? invitationExpiresAt,
        DateTime now)
    {
        if (hasAccess && accessRevokedAt is null)
        {
            return PortalClientStatus.Active;
        }

        if (accountStatus == PortalAccountStatus.Invited
            && invitationExpiresAt is { } expires && invitationAcceptedAt is null && invitationRevokedAt is null)
        {
            return expires > now ? PortalClientStatus.Invited : PortalClientStatus.InvitationExpired;
        }

        return hasAccess ? PortalClientStatus.AccessEnded : PortalClientStatus.NotInvited;
    }
}
