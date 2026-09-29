using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Auth.Abstraction;

public interface IPasswordResetRepository
{
    /// <summary>Tracked, with the user loaded.</summary>
    Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Stages a new reset link; saved by the unit of work.</summary>
    void Add(PasswordResetToken token);

    /// <summary>
    /// Marks the link used only if it is still unused and unexpired, in a single statement, so the same link can never
    /// be used twice.
    /// </summary>
    Task<bool> TryUseAsync(long tokenId, DateTime usedAt, CancellationToken cancellationToken = default);

    /// <summary>Makes every unused link of a user expire now (when a new one is sent, or the password is reset).</summary>
    Task ExpirePendingForUserAsync(long userId, DateTime now, CancellationToken cancellationToken = default);
}
