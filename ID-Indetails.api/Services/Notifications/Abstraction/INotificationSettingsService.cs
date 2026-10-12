using ID_Indetails.api.Business.Push;
using ID_Indetails.api.DTO.Request.Notifications;
using ID_Indetails.api.DTO.Response.Notifications;

namespace ID_Indetails.api.Services.Notifications.Abstraction;

/// <summary>Each person's own quiet hours and muted push categories. Nobody reads or changes anyone else's.</summary>
public interface INotificationSettingsService
{
    /// <summary>The caller's settings, or the defaults (<c>isDefault</c>) while they never saved their own.</summary>
    Task<NotificationSettingsResponse> GetAsync(CancellationToken cancellationToken = default);

    Task<NotificationSettingsResponse> SaveAsync(SaveNotificationSettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>What the push sender honours for <paramref name="userId"/>.</summary>
    Task<PushPreferences> PreferencesOfAsync(long userId, CancellationToken cancellationToken = default);
}
