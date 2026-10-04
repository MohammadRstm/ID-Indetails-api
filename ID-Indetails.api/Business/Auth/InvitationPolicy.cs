using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Auth;

/// <summary>
/// Who may invite whom, and into which domain. Decided by permissions, not roles:
/// <list type="bullet">
/// <item><c>employee:invite_super_admin</c> invites super admins, who get no domain.</item>
/// <item><c>employee:invite</c> invites admins and employees into a domain the inviter can
/// see. With only one visible domain it is used when none is given; otherwise one must be chosen.</item>
/// </list>
/// </summary>
public static class InvitationPolicy
{
    public sealed record Decision(bool Allowed, string? DomainCode, string? DenialReason)
    {
        public static Decision Allow(string? domainCode) => new(true, domainCode, null);

        public static Decision Deny(string reason) => new(false, null, reason);
    }

    /// <param name="requestedDomain">Domain asked for in the request; may be empty (see above).</param>
    public static Decision Evaluate(UserAccess inviter, string targetRole, string? requestedDomain)
    {
        if (!RoleCodes.All.Contains(targetRole))
        {
            return Decision.Deny("Unknown role.");
        }

        if (requestedDomain is not null && !DomainCodes.All.Contains(requestedDomain))
        {
            return Decision.Deny("Unknown domain.");
        }

        if (targetRole == RoleCodes.SuperAdmin)
        {
            if (!inviter.Has(PermissionCodes.EmployeeInviteSuperAdmin))
            {
                return Decision.Deny("You are not allowed to invite super admins.");
            }

            return requestedDomain is null
                ? Decision.Allow(null)
                : Decision.Deny("Super admins do not belong to a domain.");
        }

        if (!inviter.Has(PermissionCodes.EmployeeInvite))
        {
            return Decision.Deny("You are not allowed to invite people.");
        }

        var domain = requestedDomain ?? (inviter.VisibleDomains.Count == 1 ? inviter.VisibleDomains[0] : null);
        if (domain is null)
        {
            return Decision.Deny("Choose the domain the person will belong to.");
        }

        return inviter.CanSee(domain)
            ? Decision.Allow(domain)
            : Decision.Deny("You can only invite people into a domain you work in.");
    }

    /// <summary>
    /// Nobody hands out more than they hold: the role's default permissions in the domain the person joins must all be
    /// the inviter's own (super admins hold everything). Null when they are.
    /// </summary>
    public static string? CheckDefaultsHeld(UserAccess inviter, IEnumerable<string> roleDefaults)
        => PermissionChangePolicy.NotHeld(inviter, roleDefaults) is { Count: > 0 } notHeld
            ? $"You can only invite someone into a role whose permissions you hold yourself. You do not hold: {string.Join(", ", notHeld)}."
            : null;
}
