using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;

namespace ID_Indetails.api.Authorization;

/// <summary>
/// The signed-in user, read from the validated access token. Identity always comes from here,
/// never from a request body.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    long Id { get; }

    string Role { get; }

    /// <summary>Null for super admins.</summary>
    string? Domain { get; }

    string FullName { get; }

    /// <summary>The phone app's session (the <c>sid</c> claim). Null for the website's tokens.</summary>
    Guid? SessionId { get; }
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal Principal =>
        accessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal();

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    public long Id => long.TryParse(Claim(AuthClaimTypes.UserId), out var id)
        ? id
        : throw new UnauthenticatedException(ErrorCodes.Unauthenticated, "You need to sign in.");

    public string Role => Claim(AuthClaimTypes.Role)
        ?? throw new UnauthenticatedException(ErrorCodes.Unauthenticated, "You need to sign in.");

    public string? Domain => Claim(AuthClaimTypes.Domain);

    public string FullName =>
        $"{Claim(AuthClaimTypes.FirstName)} {Claim(AuthClaimTypes.LastName)}".Trim();

    public Guid? SessionId => Guid.TryParse(Claim(AuthClaimTypes.SessionId), out var id) ? id : null;

    private string? Claim(string type) => Principal.FindFirst(type)?.Value;
}
