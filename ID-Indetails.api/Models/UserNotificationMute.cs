using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>A category of pushes a person turned off. The in-app list still shows its notifications.</summary>
public class UserNotificationMute
{
    public long UserId { get; set; }

    public NotificationCategory Category { get; set; }

    public DateTime MutedAt { get; set; }

    public User User { get; set; } = null!;
}
