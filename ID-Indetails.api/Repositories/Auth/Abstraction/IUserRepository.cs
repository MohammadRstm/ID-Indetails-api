using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Auth.Abstraction;

public interface IUserRepository
{
    /// <summary>Tracked, with Team loaded.</summary>
    Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with Team loaded. <paramref name="normalizedEmail"/> must be trimmed and lower-case.</summary>
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    /// <summary>Stages a new user; saved by the unit of work.</summary>
    void Add(User user);

    /// <summary>Stages a pay-history row; saved by the unit of work.</summary>
    void AddPayHistory(UserPayHistory payHistory);
}
