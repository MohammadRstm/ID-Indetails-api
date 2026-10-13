using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Portal.Abstraction;

/// <summary>What the portal token check needs about an account, read on every portal request.</summary>
public sealed record PortalAccountSessionState(PortalAccountStatus Status, DateTime? SessionsValidFrom);

public interface IPortalAccountRepository
{
    Task<PortalAccountSessionState?> GetSessionStateAsync(long accountId, CancellationToken cancellationToken = default);

    /// <summary>Tracked.</summary>
    Task<PortalAccount?> GetByIdAsync(long accountId, CancellationToken cancellationToken = default);

    /// <summary>Tracked. The email must already be normalised.</summary>
    Task<PortalAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Untracked, for listing a project's clients.</summary>
    Task<IReadOnlyList<PortalAccount>> GetByEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken = default);

    /// <summary>Stages a new account; saved by the unit of work.</summary>
    void Add(PortalAccount account);
}
