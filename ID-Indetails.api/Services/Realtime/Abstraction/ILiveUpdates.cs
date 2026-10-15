namespace ID_Indetails.api.Services.Realtime.Abstraction;

/// <summary>Which chat changed: a team's, or the direct chat with <paramref name="UserId"/>.</summary>
public sealed record LiveChat(long? TeamId, long? UserId);

/// <summary>
/// "Something changed" signals for signed-in clients (see <c>Hubs/LiveHub</c>). Staged while a request runs and sent
/// only once it has succeeded (<c>Middlewares/LiveUpdatesMiddleware</c>); background jobs call <see cref="FlushAsync"/>.
/// </summary>
public interface ILiveUpdates
{
    /// <summary>The bell changed: a new notification, or some were read.</summary>
    void NotificationsChanged(IEnumerable<long> userIds);

    /// <summary>A chat changed for this person: a new message, or they read it on another device.</summary>
    void ChatChanged(long userId, LiveChat chat);

    /// <summary>A project's client portal discussion changed, for these staff members and these clients.</summary>
    void PortalChanged(long projectId, IEnumerable<long> userIds, IEnumerable<long> accountIds);

    /// <summary>These clients' bells changed.</summary>
    void PortalNotificationsChanged(IEnumerable<long> accountIds);

    /// <summary>These clients lost access to a project (or every project): their open pages load again.</summary>
    void PortalAccessChanged(IEnumerable<long> accountIds);

    Task FlushAsync(CancellationToken cancellationToken = default);

    void Discard();
}

/// <summary>Sends a signal to every live connection of these people: SignalR in the API, captured in tests.</summary>
public interface ILivePublisher
{
    Task PublishAsync(IReadOnlyCollection<long> userIds, string signal, object? payload, CancellationToken cancellationToken = default);
}

/// <summary>Sends a signal to every live connection of these clients (the portal hub).</summary>
public interface IPortalLivePublisher
{
    Task PublishAsync(IReadOnlyCollection<long> accountIds, string signal, object? payload, CancellationToken cancellationToken = default);
}

public static class LiveSignals
{
    public const string Notifications = "notifications";
    public const string Messages = "messages";

    /// <summary><c>{ projectId }</c>: that project's client portal discussion changed.</summary>
    public const string Portal = "portal";

    /// <summary>A client's access ended somewhere: what their pages show may no longer be theirs to see.</summary>
    public const string Access = "access";
}
