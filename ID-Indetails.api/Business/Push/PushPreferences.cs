using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Push;

/// <summary>A person's choices for their pushes: quiet hours (minutes after midnight, company time) and muted categories.</summary>
public sealed record PushPreferences(int? QuietFromMinute, int? QuietToMinute, IReadOnlySet<NotificationCategory> Muted)
{
    /// <summary>For whoever never saved their own: 22:00 to 07:00, nothing muted (decision 6).</summary>
    public static PushPreferences Default { get; } = new(QuietHours.DefaultFromMinute, QuietHours.DefaultToMinute, new HashSet<NotificationCategory>());

    /// <summary>The log-your-hours reminder has no category, so it is never muted.</summary>
    public bool Mutes(string kind) => PushCategories.Of(kind) is { } category && Muted.Contains(category);

    public bool QuietAt(TimeOnly companyTime) => QuietHours.Contains(QuietFromMinute, QuietToMinute, companyTime);
}
