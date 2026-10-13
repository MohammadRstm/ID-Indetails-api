using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;

namespace ID_Indetails.api.Authorization;

/// <summary>The signed-in client portal account, read from the validated portal token. Never from a request body.</summary>
public interface ICurrentPortalAccount
{
    long Id { get; }
}

public sealed class CurrentPortalAccount(IHttpContextAccessor accessor) : ICurrentPortalAccount
{
    public long Id => long.TryParse(accessor.HttpContext?.User.FindFirst(AuthClaimTypes.PortalAccountId)?.Value, out var id)
        ? id
        : throw new UnauthenticatedException(ErrorCodes.Unauthenticated, "You need to sign in.");
}
