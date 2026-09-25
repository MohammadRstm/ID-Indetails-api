using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>Time-tracking, project and notification rules owned by one domain's admins.</summary>
public class DomainSettings
{
    public string DomainCode { get; set; } = string.Empty;

    public EditWindow EditWindow { get; set; } = EditWindow.OneDay;

    public bool AllowOverlap { get; set; }

    public bool AllowFutureEntries { get; set; }

    public bool RequireDescription { get; set; } = true;

    public bool TimerEnabled { get; set; } = true;

    public bool LogReminder { get; set; } = true;

    public EntryNotificationMode NotifyEntry { get; set; } = EntryNotificationMode.All;

    public bool NotifyProject { get; set; } = true;

    public bool NotifyIdle { get; set; } = true;

    public bool WeeklySummary { get; set; } = true;

    /// <summary>Whether work may still be logged while a project is on hold.</summary>
    public bool HoldLogging { get; set; }

    public bool AutoArchive { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public long? UpdatedById { get; set; }

    public Domain Domain { get; set; } = null!;
}
