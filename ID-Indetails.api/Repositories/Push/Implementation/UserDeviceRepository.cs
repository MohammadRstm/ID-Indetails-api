using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Push.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Push.Implementation;

public sealed class UserDeviceRepository(IdIndetailsDbContext db) : IUserDeviceRepository
{
    public Task SaveAsync(
        long userId,
        Guid sessionId,
        string pushToken,
        DevicePlatform platform,
        string? appVersion,
        DateTime at,
        CancellationToken cancellationToken = default)
    {
        var session = sessionId.ToString();
        var provider = SnakeCaseEnum.ToDb(PushProvider.Expo);
        var platformValue = SnakeCaseEnum.ToDb(platform);
        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO `user_devices` (`user_id`, `session_id`, `provider`, `push_token`, `platform`, `app_version`, `created_at`, `updated_at`)
            VALUES ({userId}, {session}, {provider}, {pushToken}, {platformValue}, {appVersion}, {at}, {at}) AS new
            ON DUPLICATE KEY UPDATE `user_id` = new.`user_id`, `session_id` = new.`session_id`, `provider` = new.`provider`,
                `platform` = new.`platform`, `app_version` = new.`app_version`, `updated_at` = new.`updated_at`
            """,
            cancellationToken);
    }

    public Task DeleteOtherTokensOfSessionAsync(Guid sessionId, string keptToken, CancellationToken cancellationToken = default)
        => db.UserDevices.Where(d => d.SessionId == sessionId && d.PushToken != keptToken).ExecuteDeleteAsync(cancellationToken);

    public Task<UserDevice?> GetByTokenAsync(string pushToken, CancellationToken cancellationToken = default)
        => db.UserDevices.AsNoTracking().SingleOrDefaultAsync(d => d.PushToken == pushToken, cancellationToken);

    public Task<UserDevice?> GetForUserAsync(long id, long userId, CancellationToken cancellationToken = default)
        => db.UserDevices.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<UserDevice>> ListLiveForUserAsync(long userId, DateTime now, CancellationToken cancellationToken = default)
        => await Live(now)
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UserDevice>> ListLiveForUsersAsync(
        IReadOnlyCollection<long> userIds,
        DateTime now,
        CancellationToken cancellationToken = default)
        => await Live(now).AsNoTracking().Where(d => userIds.Contains(d.UserId)).ToListAsync(cancellationToken);

    public Task DeleteByTokenAsync(string pushToken, CancellationToken cancellationToken = default)
        => db.UserDevices.Where(d => d.PushToken == pushToken).ExecuteDeleteAsync(cancellationToken);

    public Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => db.UserDevices.Where(d => d.SessionId == sessionId).ExecuteDeleteAsync(cancellationToken);

    public Task DeleteSessionOfUserAsync(long userId, Guid sessionId, CancellationToken cancellationToken = default)
        => db.UserDevices.Where(d => d.UserId == userId && d.SessionId == sessionId).ExecuteDeleteAsync(cancellationToken);

    public Task<int> DeleteDeadAsync(DateTime now, CancellationToken cancellationToken = default)
        => db.UserDevices
            .Where(d => !db.RefreshTokens.Any(t => t.SessionId == d.SessionId && t.UserId == d.UserId && t.RevokedAt == null && t.ExpiresAt > now))
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>Devices whose session, of the same person, still has a valid refresh token.</summary>
    private IQueryable<UserDevice> Live(DateTime now)
        => db.UserDevices.Where(d =>
            db.RefreshTokens.Any(t => t.SessionId == d.SessionId && t.UserId == d.UserId && t.RevokedAt == null && t.ExpiresAt > now));
}
