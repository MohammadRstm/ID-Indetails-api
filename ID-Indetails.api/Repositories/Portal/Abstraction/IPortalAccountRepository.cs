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

    /// <summary>
    /// Tracked, and locked (<c>SELECT … FOR UPDATE</c>) until the transaction ends, like <see cref="LockAsync"/>. Call
    /// it first in the transaction, so what is read after it is current. The email must already be normalised.
    /// </summary>
    Task<PortalAccount?> GetByEmailForUpdateAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the account (<c>SELECT … FOR UPDATE</c>) until the transaction ends, so that inviting, resending and
    /// accepting for one account take turns. Call it first in the transaction, so what is read after it is current.
    /// </summary>
    Task LockAsync(long accountId, CancellationToken cancellationToken = default);

    /// <summary>Untracked, for listing a project's clients.</summary>
    Task<IReadOnlyList<PortalAccount>> GetByEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken = default);

    /// <summary>Stages a new account; saved by the unit of work.</summary>
    void Add(PortalAccount account);

    /// <summary>
    /// Counts a wrong password in the database right away, so wrong passwords sent at once all count. When the count
    /// reaches <paramref name="attempts"/>, sign-in pauses until <paramref name="pausedUntil"/> and the count starts over.
    /// Returns whether this call paused it.
    /// </summary>
    Task<bool> CountWrongPasswordAsync(long accountId, int attempts, DateTime pausedUntil, CancellationToken cancellationToken = default);
}
