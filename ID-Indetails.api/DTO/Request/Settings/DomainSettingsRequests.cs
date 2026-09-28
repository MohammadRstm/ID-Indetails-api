using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Settings;

/// <summary>Replaces every setting of the domain.</summary>
public sealed record SaveDomainSettingsRequest
{
    public EditWindow EditWindow { get; init; }

    public bool AllowOverlap { get; init; }

    public bool AllowFutureEntries { get; init; }

    public bool RequireDescription { get; init; }

    public bool TimerEnabled { get; init; }

    public bool LogReminder { get; init; }

    public EntryNotificationMode NotifyEntry { get; init; }

    public bool NotifyProject { get; init; }

    public bool NotifyIdle { get; init; }

    public bool WeeklySummary { get; init; }

    public bool HoldLogging { get; init; }

    public bool AutoArchive { get; init; }
}
