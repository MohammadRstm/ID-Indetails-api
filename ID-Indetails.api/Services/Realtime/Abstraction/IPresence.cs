namespace ID_Indetails.api.Services.Realtime.Abstraction;

/// <summary>
/// Who is online: someone with the live connection (<c>Hubs/LiveHub</c>) open. Kept in memory on this API instance,
/// which is one for now (C5); several would need to share it. Also spaces out the "last seen" writes.
/// </summary>
public interface IPresence
{
    void Connected(long userId);

    /// <returns>Whether that was their last open connection.</returns>
    bool Disconnected(long userId);

    bool IsOnline(long userId);

    IReadOnlyCollection<long> OnlineUserIds();

    /// <summary>Whether to record the person as seen now: at most once every few minutes each.</summary>
    bool ShouldRecordSeen(long userId, DateTime now);
}
