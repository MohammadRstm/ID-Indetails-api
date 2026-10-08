using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Push.Abstraction;

/// <summary>
/// Push devices. "Live" means the device's session still has a valid refresh token: only those are listed or sent to.
/// Every write runs immediately.
/// </summary>
public interface IUserDeviceRepository
{
    /// <summary>
    /// Registers <paramref name="pushToken"/> for the person's session in one statement: a token registered before,
    /// to anyone, moves to them.
    /// </summary>
    Task SaveAsync(
        long userId,
        Guid sessionId,
        string pushToken,
        DevicePlatform platform,
        string? appVersion,
        DateTime at,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the session's devices with another token: the phone's earlier one.</summary>
    Task DeleteOtherTokensOfSessionAsync(Guid sessionId, string keptToken, CancellationToken cancellationToken = default);

    /// <summary>Not tracked.</summary>
    Task<UserDevice?> GetByTokenAsync(string pushToken, CancellationToken cancellationToken = default);

    /// <summary>One of the person's devices, live or not. Not tracked.</summary>
    Task<UserDevice?> GetForUserAsync(long id, long userId, CancellationToken cancellationToken = default);

    /// <summary>The person's live devices, newest first. Not tracked.</summary>
    Task<IReadOnlyList<UserDevice>> ListLiveForUserAsync(long userId, DateTime now, CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task DeleteSessionOfUserAsync(long userId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every device that isn't live, and says how many.</summary>
    Task<int> DeleteDeadAsync(DateTime now, CancellationToken cancellationToken = default);
}
