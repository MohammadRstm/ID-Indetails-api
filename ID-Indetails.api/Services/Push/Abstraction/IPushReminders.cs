namespace ID_Indetails.api.Services.Push.Abstraction;

/// <summary>
/// The push-only reminders, run by the notification scheduler's hourly checks. They write no notification row, so the
/// website's bell is unchanged. Each goes to a person at most once a day.
/// </summary>
public interface IPushReminders
{
    /// <summary>
    /// Design people who log hours, in the last hour of their working day when they have logged less than it requires.
    /// Follows Design's <c>logReminder</c> setting.
    /// </summary>
    Task LogHoursAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// On Saturday from 15:00, Execution people with <c>labour_report:create</c>, for each active site they recorded
    /// anything on in the last 4 weeks whose current week isn't reported yet.
    /// </summary>
    Task LabourWeeksAsync(CancellationToken cancellationToken = default);
}
