namespace ID_Indetails.api.Services.Notifications.Abstraction;

/// <summary>What a notification is about. The client writes the text from the kind and the payload.</summary>
/// <param name="DomainCode">The domain it belongs to; only people who can see it receive it. Null for company-level ones.</param>
/// <param name="Payload">Serialised as JSON. Never money.</param>
public sealed record NotificationDraft(
    string Kind,
    long ActorId,
    string? DomainCode,
    long? ProjectId,
    string? SubjectType,
    long? SubjectId,
    object? Payload);

/// <summary>Every notification goes through here, so the rules about who may receive one hold everywhere.</summary>
public interface INotificationSender
{
    /// <summary>
    /// Stages one notification for each recipient, saved by the unit of work. Skips the actor, repeats, and anyone
    /// deactivated or unable to see the draft's domain.
    /// </summary>
    Task SendAsync(NotificationDraft draft, IEnumerable<long> recipientIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="SendAsync"/>, but keeps one rolling notification per recipient and <paramref name="digestKey"/>:
    /// a repeat adds 1 to its count, takes the new payload and actor, and shows as unread and new again.
    /// </summary>
    Task SendDigestAsync(NotificationDraft draft, string digestKey, IEnumerable<long> recipientIds, CancellationToken cancellationToken = default);

    /// <summary>Active people whose home domain is <paramref name="domain"/>: its admins and employees, not super admins.</summary>
    Task<IReadOnlyList<long>> DomainMembersAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>Active people who hold <paramref name="permission"/> and can see <paramref name="domain"/>, super admins included.</summary>
    Task<IReadOnlyList<long>> HoldersAsync(string domain, string permission, CancellationToken cancellationToken = default);
}
