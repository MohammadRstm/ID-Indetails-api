using System.Globalization;
using ID_Indetails.api.Hubs;
using ID_Indetails.api.Services.Realtime.Abstraction;
using Microsoft.AspNetCore.SignalR;

namespace ID_Indetails.api.Services.Realtime.Implementation;

/// <summary>Collects a request's signals, each person once per signal, and sends them together.</summary>
public sealed class LiveUpdates(ILivePublisher publisher) : ILiveUpdates
{
    private readonly HashSet<long> _notifications = [];
    private readonly HashSet<(long UserId, LiveChat Chat)> _chats = [];

    public void NotificationsChanged(IEnumerable<long> userIds) => _notifications.UnionWith(userIds);

    public void ChatChanged(long userId, LiveChat chat) => _chats.Add((userId, chat));

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_notifications.Count > 0)
        {
            await publisher.PublishAsync(_notifications.ToList(), LiveSignals.Notifications, null, cancellationToken);
        }

        foreach (var chat in _chats.GroupBy(c => c.Chat))
        {
            await publisher.PublishAsync(chat.Select(c => c.UserId).ToList(), LiveSignals.Messages, chat.Key, cancellationToken);
        }

        Discard();
    }

    public void Discard()
    {
        _notifications.Clear();
        _chats.Clear();
    }
}

public sealed class SignalRLivePublisher(IHubContext<LiveHub> hub) : ILivePublisher
{
    public Task PublishAsync(IReadOnlyCollection<long> userIds, string signal, object? payload, CancellationToken cancellationToken = default)
    {
        var clients = hub.Clients.Users(userIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList());
        return payload is null
            ? clients.SendCoreAsync(signal, [], cancellationToken)
            : clients.SendAsync(signal, payload, cancellationToken);
    }
}
