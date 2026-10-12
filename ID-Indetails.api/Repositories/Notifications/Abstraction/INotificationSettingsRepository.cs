using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Notifications.Abstraction;

/// <summary>A person's own push settings. Every write runs immediately.</summary>
public interface INotificationSettingsRepository
{
    /// <summary>Not tracked; null when the person never saved theirs.</summary>
    Task<UserNotificationSettings?> GetAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<NotificationCategory>> MutedAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces the quiet hours in one statement, so two saves at once can't collide.</summary>
    Task SaveQuietHoursAsync(long userId, short? fromMinute, short? toMinute, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Makes <paramref name="categories"/> the muted ones: others are unmuted, ones already muted keep when.</summary>
    Task ReplaceMutedAsync(long userId, IReadOnlyCollection<NotificationCategory> categories, DateTime at, CancellationToken cancellationToken = default);
}
