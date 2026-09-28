using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Auth.Abstraction;

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

    /// <summary>Stages a new user; saved by the unit of work.</summary>
    void Add(User user);

    /// <summary>Stages a pay-history row; saved by the unit of work.</summary>
    void AddPayHistory(UserPayHistory payHistory);
}

public sealed record UserRoleSnapshot(long Id, string RoleCode, string? DomainCode, UserStatus Status);
