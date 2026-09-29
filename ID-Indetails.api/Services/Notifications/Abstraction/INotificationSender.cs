namespace ID_Indetails.api.Services.Notifications.Abstraction;

/// <summary>What a notification is about. The client writes the text from the kind and the payload.</summary>
/// <param name="DomainCode">The domain it belongs to; only people who can see it receive it.</param>
/// <param name="Payload">Serialised as JSON. Never money.</param>
public sealed record NotificationDraft(
    string Kind,
    long ActorId,
    string DomainCode,
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

    /// <summary>Active people who hold <paramref name="permission"/> and can see <paramref name="domain"/>, super admins included.</summary>
    Task<IReadOnlyList<long>> HoldersAsync(string domain, string permission, CancellationToken cancellationToken = default);
}
