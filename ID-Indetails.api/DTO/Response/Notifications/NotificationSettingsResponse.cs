using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Notifications;

/// <param name="QuietFromMinute">Minutes after midnight, company time; null with <paramref name="QuietToMinute"/> for no quiet hours.</param>
/// <param name="IsDefault">True while the person has never saved their own (22:00 to 07:00, nothing muted).</param>
public sealed record NotificationSettingsResponse(
    int? QuietFromMinute,
    int? QuietToMinute,
    IReadOnlyList<NotificationCategory> MutedCategories,
    bool IsDefault);
