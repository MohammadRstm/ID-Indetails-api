using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Auth.Abstraction;

/// <param name="DomainCode">Null for super admins.</param>
public sealed record UserDomain(long Id, string? DomainCode);

public interface IUserRepository
{
    /// <summary>Tracked, with Team loaded.</summary>
    Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracked, and locked (<c>SELECT … FOR UPDATE</c>) until the current transaction ends, so
    /// two changes to the same user run one after the other. Call inside a transaction.
    /// </summary>
    Task<User?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Just what permission checks need, not tracked.</summary>
    Task<UserRoleSnapshot?> GetRoleSnapshotAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with Team loaded. <paramref name="normalizedEmail"/> must be trimmed and lower-case.</summary>
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    /// <summary>Ids of the active people whose home domain is <paramref name="domain"/> (super admins have none).</summary>
    Task<IReadOnlyList<long>> ActiveIdsInDomainAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>The active people among these, with their domain. Not tracked.</summary>
    Task<IReadOnlyList<UserDomain>> ActiveDomainsAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts one more wrong password; at <paramref name="attempts"/> the count starts again and sign-in is paused until
    /// <paramref name="pausedUntil"/>. True when this call paused it.
    /// </summary>
    Task<bool> CountWrongPasswordAsync(long userId, int attempts, DateTime pausedUntil, CancellationToken cancellationToken = default);

    /// <summary>Records when the person was last seen, straight in the database.</summary>
    Task SetLastSeenAsync(long id, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Stages a new user; saved by the unit of work.</summary>
    void Add(User user);

    /// <summary>Stages a pay-history row; saved by the unit of work.</summary>
    void AddPayHistory(UserPayHistory payHistory);
}

public sealed record UserRoleSnapshot(
    long Id, string RoleCode, string? DomainCode, UserStatus Status, DateTime? SessionsRevokedAt, DateTime? SessionsValidFrom);
