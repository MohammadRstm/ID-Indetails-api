namespace ID_Indetails.api.Services.Notifications.Abstraction;

public enum ScheduledCheck
{
    WeeklySummary,
    IdleProjects,
    RequirementDeadlines,
    OverdueInvoices,
    ShortHours,
    CostWarnings,
    DraftInvoices,
    AutoArchive,
    ScheduledInvoices,
}

/// <summary>
/// The notifications nobody's action sends, and the domains' auto-archiving. Each notification is keyed, so a check
/// can run as often as it likes and still sends each notification once.
/// </summary>
public interface IScheduledNotifications
{
    /// <summary>Runs one check and saves what it sends.</summary>
    Task RunAsync(ScheduledCheck check, CancellationToken cancellationToken = default);
}
