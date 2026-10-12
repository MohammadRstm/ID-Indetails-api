namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>
/// Which reminders went out today. In memory: after a restart a reminder can repeat once, which is accepted rather than
/// keeping a table of them.
/// </summary>
public sealed class ReminderLog
{
    private readonly Dictionary<(long UserId, string Kind, long? ProjectId), DateOnly> _sentOn = [];
    private readonly Lock _gate = new();

    public bool WasSent(long userId, string kind, long? projectId, DateOnly day)
    {
        lock (_gate)
        {
            return _sentOn.TryGetValue((userId, kind, projectId), out var sentOn) && sentOn == day;
        }
    }

    /// <summary>Records the reminder for <paramref name="day"/>; false when it was already sent that day.</summary>
    public bool TryMark(long userId, string kind, long? projectId, DateOnly day)
    {
        lock (_gate)
        {
            var key = (userId, kind, projectId);
            if (_sentOn.TryGetValue(key, out var sentOn) && sentOn == day)
            {
                return false;
            }

            _sentOn[key] = day;
            return true;
        }
    }
}
