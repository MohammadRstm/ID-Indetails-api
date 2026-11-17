namespace ID_Indetails.api.Services.Meetings.Abstraction;

public interface IMeetingReminders
{
    /// <summary>Stages the reminders now due; the caller saves them.</summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
