using System.Collections.Concurrent;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.api.Services.Realtime.Implementation;

public sealed class Presence : IPresence
{
    public static readonly TimeSpan SeenEvery = TimeSpan.FromMinutes(5);

    // Open connections per person: a person with two tabs stays online until both close.
    private readonly ConcurrentDictionary<long, int> _connections = new();
    private readonly ConcurrentDictionary<long, DateTime> _seen = new();

    public void Connected(long userId) => _connections.AddOrUpdate(userId, 1, (_, count) => count + 1);

    public bool Disconnected(long userId)
    {
        while (_connections.TryGetValue(userId, out var count))
        {
            if (count <= 1)
            {
                if (_connections.TryRemove(new KeyValuePair<long, int>(userId, count)))
                {
                    return true;
                }
            }
            else if (_connections.TryUpdate(userId, count - 1, count))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsOnline(long userId) => _connections.ContainsKey(userId);

    public IReadOnlyCollection<long> OnlineUserIds() => _connections.Keys.ToList();

    public bool ShouldRecordSeen(long userId, DateTime now)
    {
        if (_seen.TryGetValue(userId, out var last) && now - last < SeenEvery)
        {
            return false;
        }

        // Two requests at once may both record; that's harmless.
        _seen[userId] = now;
        return true;
    }
}
