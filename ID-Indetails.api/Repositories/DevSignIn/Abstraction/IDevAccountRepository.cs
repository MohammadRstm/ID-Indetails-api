namespace ID_Indetails.api.Repositories.DevSignIn.Abstraction;

/// <summary>A staff member the development sign-in lists.</summary>
public sealed record DevStaffAccount(
    long Id,
    string Email,
    string? FirstName,
    string? LastName,
    string RoleCode,
    string? DomainCode,
    string? Position,
    string? TeamName);

/// <summary>A client's portal account the development sign-in lists.</summary>
public sealed record DevPortalAccount(long Id, string Email, string? FirstName, string? LastName);

/// <summary>Development only: the accounts one can sign in as.</summary>
public interface IDevAccountRepository
{
    /// <summary>Staff who can sign in: active, and not suspended. Not tracked.</summary>
    Task<IReadOnlyList<DevStaffAccount>> SignableStaffAsync(CancellationToken cancellationToken = default);

    /// <summary>Clients' active portal accounts. Not tracked.</summary>
    Task<IReadOnlyList<DevPortalAccount>> ActivePortalAccountsAsync(CancellationToken cancellationToken = default);
}
