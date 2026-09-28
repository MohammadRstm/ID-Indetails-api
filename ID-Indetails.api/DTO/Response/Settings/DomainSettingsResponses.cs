using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Settings;

/// <param name="UpdatedAt">Null while the domain still uses the defaults.</param>
public sealed record DomainSettingsResponse(
    string DomainCode,
    EditWindow EditWindow,
    bool AllowOverlap,
    bool AllowFutureEntries,
    bool RequireDescription,
    bool TimerEnabled,
    bool LogReminder,
    EntryNotificationMode NotifyEntry,
    bool NotifyProject,
    bool NotifyIdle,
    bool WeeklySummary,
    bool HoldLogging,
    bool AutoArchive,
    DateTime? UpdatedAt);
