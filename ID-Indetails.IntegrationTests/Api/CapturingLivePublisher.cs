using System.Collections.Concurrent;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>Records live signals instead of sending them over SignalR.</summary>
public sealed class CapturingLivePublisher : ILivePublisher
{
    private readonly ConcurrentQueue<(long UserId, string Signal, object? Payload)> _sent = new();

    public Task PublishAsync(IReadOnlyCollection<long> userIds, string signal, object? payload, CancellationToken cancellationToken = default)
    {
        foreach (var userId in userIds)
        {
            _sent.Enqueue((userId, signal, payload));
        }

        return Task.CompletedTask;
    }

    /// <summary>The payloads of every <paramref name="signal"/> sent to this person so far.</summary>
    public IReadOnlyList<object?> SentTo(long userId, string signal)
        => _sent.Where(s => s.UserId == userId && s.Signal == signal).Select(s => s.Payload).ToList();
}
