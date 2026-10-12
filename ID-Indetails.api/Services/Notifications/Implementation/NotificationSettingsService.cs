using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Push;
using ID_Indetails.api.DTO.Request.Notifications;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;

namespace ID_Indetails.api.Services.Notifications.Implementation;

/// <summary>
/// Quiet hours deliver pushes silently rather than holding them back; a muted category sends no pushes at all. Neither
/// changes the in-app list.
/// </summary>
public sealed class NotificationSettingsService(
    ICurrentUser currentUser,
    INotificationSettingsRepository settings,
    IUnitOfWork unitOfWork,
    IClock clock) : INotificationSettingsService
{
    public async Task<NotificationSettingsResponse> GetAsync(CancellationToken cancellationToken = default)
        => await DescribeAsync(currentUser.Id, cancellationToken);

    public async Task<NotificationSettingsResponse> SaveAsync(SaveNotificationSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            await settings.SaveQuietHoursAsync(
                currentUser.Id, (short?)request.QuietFromMinute, (short?)request.QuietToMinute, now, cancellationToken);
            await settings.ReplaceMutedAsync(currentUser.Id, request.MutedCategories.Distinct().ToList(), now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(currentUser.Id, cancellationToken);
    }

    public async Task<PushPreferences> PreferencesOfAsync(long userId, CancellationToken cancellationToken = default)
    {
        var saved = await settings.GetAsync(userId, cancellationToken);
        return saved is null
            ? PushPreferences.Default
            : new PushPreferences(saved.QuietFromMin, saved.QuietToMin, (await settings.MutedAsync(userId, cancellationToken)).ToHashSet());
    }

    private async Task<NotificationSettingsResponse> DescribeAsync(long userId, CancellationToken cancellationToken)
    {
        var saved = await settings.GetAsync(userId, cancellationToken);
        return saved is null
            ? new NotificationSettingsResponse(QuietHours.DefaultFromMinute, QuietHours.DefaultToMinute, [], IsDefault: true)
            : new NotificationSettingsResponse(saved.QuietFromMin, saved.QuietToMin, await settings.MutedAsync(userId, cancellationToken), IsDefault: false);
    }
}
