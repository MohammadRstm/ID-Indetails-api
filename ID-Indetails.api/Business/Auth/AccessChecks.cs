using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;

namespace ID_Indetails.api.Business.Auth;

/// <summary>
/// The permission an endpoint's attribute asks for, checked again by the service behind it: a caller that isn't that
/// endpoint (a job, a hub, another service, a new endpoint missing the attribute) is refused rather than let through.
/// </summary>
public static class AccessChecks
{
    /// <summary>The caller, once they hold at least one of <paramref name="anyOf"/>; otherwise 403, as the endpoint answers.</summary>
    public static UserAccess Require(this UserAccess actor, params string[] anyOf)
        => anyOf.Any(actor.Has) ? actor : throw new ForbiddenException(ErrorCodes.Forbidden, "You don't have permission to do this.");
}
