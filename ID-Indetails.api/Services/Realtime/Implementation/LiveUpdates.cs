using System.Globalization;
using ID_Indetails.api.Hubs;
using ID_Indetails.api.Services.Realtime.Abstraction;
using Microsoft.AspNetCore.SignalR;

namespace ID_Indetails.api.Services.Realtime.Implementation;

/// <summary>Collects a request's signals, each person once per signal, and sends them together.</summary>
public sealed class LiveUpdates(ILivePublisher publisher, IPortalLivePublisher portalPublisher) : ILiveUpdates
{
    private readonly HashSet<long> _notifications = [];
    private readonly HashSet<(long UserId, LiveChat Chat)> _chats = [];
    private readonly HashSet<(long ProjectId, long UserId)> _portalStaff = [];
    private readonly HashSet<(long ProjectId, long AccountId)> _portalClients = [];
    private readonly HashSet<long> _portalNotifications = [];
    private readonly HashSet<long> _portalAccess = [];

    public void NotificationsChanged(IEnumerable<long> userIds) => _notifications.UnionWith(userIds);

    public void ChatChanged(long userId, LiveChat chat) => _chats.Add((userId, chat));

    public void PortalChanged(long projectId, IEnumerable<long> userIds, IEnumerable<long> accountIds)
    {
        _portalStaff.UnionWith(userIds.Select(id => (projectId, id)));
        _portalClients.UnionWith(accountIds.Select(id => (projectId, id)));
    }

    public void PortalNotificationsChanged(IEnumerable<long> accountIds) => _portalNotifications.UnionWith(accountIds);

    public void PortalAccessChanged(IEnumerable<long> accountIds) => _portalAccess.UnionWith(accountIds);

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

        foreach (var project in _portalStaff.GroupBy(p => p.ProjectId))
        {
            await publisher.PublishAsync(project.Select(p => p.UserId).ToList(), LiveSignals.Portal, new { projectId = project.Key }, cancellationToken);
        }

        foreach (var project in _portalClients.GroupBy(p => p.ProjectId))
        {
            await portalPublisher.PublishAsync(project.Select(p => p.AccountId).ToList(), LiveSignals.Portal, new { projectId = project.Key }, cancellationToken);
        }

        if (_portalNotifications.Count > 0)
        {
            await portalPublisher.PublishAsync(_portalNotifications.ToList(), LiveSignals.Notifications, null, cancellationToken);
        }

        if (_portalAccess.Count > 0)
        {
            await portalPublisher.PublishAsync(_portalAccess.ToList(), LiveSignals.Access, null, cancellationToken);
        }

        Discard();
    }

    public void Discard()
    {
        _notifications.Clear();
        _chats.Clear();
        _portalStaff.Clear();
        _portalClients.Clear();
        _portalNotifications.Clear();
        _portalAccess.Clear();
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

public sealed class SignalRPortalLivePublisher(IHubContext<PortalHub> hub) : IPortalLivePublisher
{
    public Task PublishAsync(IReadOnlyCollection<long> accountIds, string signal, object? payload, CancellationToken cancellationToken = default)
    {
        var clients = hub.Clients.Groups(accountIds.Select(PortalHub.Group).ToList());
        return payload is null
            ? clients.SendCoreAsync(signal, [], cancellationToken)
            : clients.SendAsync(signal, payload, cancellationToken);
    }
}
