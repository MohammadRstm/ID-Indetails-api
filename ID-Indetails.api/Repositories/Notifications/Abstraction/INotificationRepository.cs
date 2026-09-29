using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Notifications.Abstraction;

/// <summary>A notification as its recipient reads it, with the actor's name and the project's title.</summary>
public sealed record NotificationView(
    long Id,
    string Kind,
    long? ActorId,
    string? ActorName,
    long? ProjectId,
    string? ProjectTitle,
    string? ProjectDomain,
    string? SubjectType,
    long? SubjectId,
    string? Payload,
    int Count,
    DateTime CreatedAt,
    DateTime? ReadAt);

public interface INotificationRepository
{
    /// <summary>Stages a new notification; saved by the unit of work.</summary>
    void Add(Notification notification);

    /// <summary>A recipient's rolling digest notification. Tracked.</summary>
    Task<Notification?> GetDigestForUpdateAsync(long recipientId, string digestKey, CancellationToken cancellationToken = default);

    /// <summary>Which of <paramref name="recipientIds"/> already have a notification with this digest key.</summary>
    Task<IReadOnlySet<long>> RecipientsWithKeyAsync(
        string digestKey,
        IReadOnlyCollection<long> recipientIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Active people who might hold <paramref name="permissionCode"/> in the domain: its members, super admins,
    /// and anyone granted it personally. The caller still checks each one's effective permissions.
    /// </summary>
    Task<IReadOnlyList<long>> PeopleWhoMayHoldAsync(string domainCode, string permissionCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of the recipient's notifications, newest first. Ones about a project of a domain outside
    /// <paramref name="visibleDomains"/> are left out. Not tracked.
    /// </summary>
    Task<(IReadOnlyList<NotificationView> Items, int TotalCount)> SearchAsync(
        long recipientId,
        IReadOnlyList<string> visibleDomains,
        bool unreadOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Unread notifications, counted the way <see cref="SearchAsync"/> lists them.</summary>
    Task<int> CountUnreadAsync(long recipientId, IReadOnlyList<string> visibleDomains, CancellationToken cancellationToken = default);

    /// <summary>One of the recipient's notifications, if <see cref="SearchAsync"/> would list it. Tracked.</summary>
    Task<Notification?> GetForUpdateAsync(long id, long recipientId, IReadOnlyList<string> visibleDomains, CancellationToken cancellationToken = default);

    /// <summary>Marks every unread notification of the recipient read, straight in the database.</summary>
    Task MarkAllReadAsync(long recipientId, DateTime readAt, CancellationToken cancellationToken = default);
}
