using ID_Indetails.api.DTO.Response.DevSignIn;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;

namespace ID_Indetails.api.Services.DevSignIn.Abstraction;

/// <summary>
/// Development only: signs in as any account that can sign in, without its password, so testing the app as each kind of
/// user needs no list of passwords. Registered only when the development sign-in is on (<c>AddDevSignIn</c>).
/// </summary>
public interface IDevSignInService
{
    /// <summary>
    /// The accounts that can sign in, in groups: super admins, then each domain's admins and employees, then clients.
    /// Names A–Z within each group.
    /// </summary>
    Task<DevAccountsResponse> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>A new website session for an active, unsuspended staff member; 404 for anyone else.</summary>
    Task<AuthSession> SignInAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>A new portal session for an active portal account; 404 for anyone else.</summary>
    Task<PortalAuthSession> PortalSignInAsync(long accountId, CancellationToken cancellationToken = default);
}
