using System.Collections.Concurrent;
using ID_Indetails.api.Business.Push;

namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>When each person last got a full push of each digest kind (<see cref="DigestPushes"/>). In memory, per API instance.</summary>
public sealed class PushThrottle
{
    private readonly ConcurrentDictionary<(long UserId, string Kind), DateTime> _lastFull = new();

    /// <summary>Whether this digest push goes out in full, and if so records it.</summary>
    public bool TakeFull(long userId, string kind, DateTime now)
    {
        var key = (userId, kind);
        if (!DigestPushes.PushesInFull(_lastFull.TryGetValue(key, out var last) ? last : null, now))
        {
            return false;
        }

        _lastFull[key] = now;
        return true;
    }
}
