using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Notifications;

/// <summary>The caller's quiet hours (both set or both null, minutes after midnight in company time) and muted categories.</summary>
public sealed record SaveNotificationSettingsRequest
{
    public int? QuietFromMinute { get; init; }

    public int? QuietToMinute { get; init; }

    public IReadOnlyList<NotificationCategory> MutedCategories { get; init; } = [];
}
