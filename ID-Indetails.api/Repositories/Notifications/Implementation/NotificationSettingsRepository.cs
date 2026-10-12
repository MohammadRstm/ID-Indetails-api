using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Notifications.Implementation;

public sealed class NotificationSettingsRepository(IdIndetailsDbContext db) : INotificationSettingsRepository
{
    public Task<UserNotificationSettings?> GetAsync(long userId, CancellationToken cancellationToken = default)
        => db.UserNotificationSettings.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<NotificationCategory>> MutedAsync(long userId, CancellationToken cancellationToken = default)
        => await db.UserNotificationMutes.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.MutedAt)
            .Select(m => m.Category)
            .ToListAsync(cancellationToken);

    public Task SaveQuietHoursAsync(long userId, short? fromMinute, short? toMinute, DateTime at, CancellationToken cancellationToken = default)
        => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO `user_notification_settings` (`user_id`, `quiet_from_min`, `quiet_to_min`, `updated_at`) VALUES ({userId}, {fromMinute}, {toMinute}, {at}) AS new
            ON DUPLICATE KEY UPDATE `quiet_from_min` = new.`quiet_from_min`, `quiet_to_min` = new.`quiet_to_min`, `updated_at` = new.`updated_at`
            """,
            cancellationToken);

    public async Task ReplaceMutedAsync(
        long userId,
        IReadOnlyCollection<NotificationCategory> categories,
        DateTime at,
        CancellationToken cancellationToken = default)
    {
        var keep = categories.ToList();
        await db.UserNotificationMutes.Where(m => m.UserId == userId && !keep.Contains(m.Category)).ExecuteDeleteAsync(cancellationToken);
        foreach (var category in keep)
        {
            var value = SnakeCaseEnum.ToDb(category);
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO `user_notification_mutes` (`user_id`, `category`, `muted_at`) VALUES ({userId}, {value}, {at}) AS new
                ON DUPLICATE KEY UPDATE `muted_at` = `user_notification_mutes`.`muted_at`
                """,
                cancellationToken);
        }
    }
}
