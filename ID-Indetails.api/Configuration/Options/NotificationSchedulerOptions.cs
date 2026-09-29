using System.ComponentModel.DataAnnotations;

namespace ID_Indetails.api.Configuration.Options;

/// <summary>How often the scheduled notifications are checked. Integration tests switch the timer off and run the checks themselves.</summary>
public sealed class NotificationSchedulerOptions
{
    public const string SectionName = "NotificationScheduler";

    public bool Enabled { get; init; } = true;

    [Range(1, 1440)]
    public int IntervalMinutes { get; init; } = 60;
}
