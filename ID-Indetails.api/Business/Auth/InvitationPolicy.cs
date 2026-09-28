using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Auth;

/// <summary>
/// Who may invite whom, and into which domain.
/// <list type="bullet">
/// <item>Super admins invite any role into either domain; a super admin gets no domain.</item>
/// <item>Admins invite admins and employees into their own domain only.</item>
/// <item>Employees invite no one.</item>
/// </list>
/// </summary>
public static class InvitationPolicy
{
    public sealed record Decision(bool Allowed, string? DomainCode, string? DenialReason)
    {
        public static Decision Allow(string? domainCode) => new(true, domainCode, null);

        public static Decision Deny(string reason) => new(false, null, reason);
    }

    /// <param name="requestedDomain">
    /// Domain asked for in the request. Admins may leave it empty (their own domain is used).
    /// </param>
    public static Decision Evaluate(
        string inviterRole,
        string? inviterDomain,
        string targetRole,
        string? requestedDomain)
    {
        if (!RoleCodes.All.Contains(targetRole))
        {
            return Decision.Deny("Unknown role.");
        }

        if (requestedDomain is not null && !DomainCodes.All.Contains(requestedDomain))
        {
            return Decision.Deny("Unknown domain.");
        }

        switch (inviterRole)
        {
            case RoleCodes.SuperAdmin when targetRole == RoleCodes.SuperAdmin:
                return requestedDomain is null
                    ? Decision.Allow(null)
                    : Decision.Deny("Super admins do not belong to a domain.");

            case RoleCodes.SuperAdmin:
                return requestedDomain is null
                    ? Decision.Deny("Choose the domain the person will belong to.")
                    : Decision.Allow(requestedDomain);

            case RoleCodes.Admin when targetRole == RoleCodes.SuperAdmin:
                return Decision.Deny("Only super admins can invite super admins.");

            case RoleCodes.Admin when inviterDomain is null:
                return Decision.Deny("Your account has no domain.");

            case RoleCodes.Admin when requestedDomain is not null && requestedDomain != inviterDomain:
                return Decision.Deny("You can only invite people into your own domain.");

            case RoleCodes.Admin:
                return Decision.Allow(inviterDomain);

            default:
                return Decision.Deny("You are not allowed to invite people.");
        }
    }
}
