using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Push.Abstraction;

/// <summary>
/// The pushes a request or a scheduled check causes. Like live signals they are staged while it runs and handed to the
/// sender only once it has succeeded and its changes are committed (<c>Middlewares/LiveUpdatesMiddleware</c>); background
/// work calls <see cref="FlushAsync"/> itself. Who receives what, and the text, are decided when the push is sent.
/// </summary>
public interface IPushQueue
{
    /// <summary>
    /// New or updated notification rows; their ids are read once saved. <paramref name="digest"/> marks rolling digests,
    /// which push at most once an hour per person.
    /// </summary>
    void Notifications(IEnumerable<Notification> rows, bool digest = false);

    /// <summary>A message, to each of <paramref name="recipientIds"/>; <paramref name="mentionedIds"/> were mentioned in it.</summary>
    void Message(long messageId, long? teamId, IEnumerable<long> recipientIds, IEnumerable<long> mentionedIds);

    /// <summary>A push with no notification row: one of the reminders.</summary>
    void Reminder(long userId, string kind, PushTarget target, string? projectTitle);

    Task FlushAsync(CancellationToken cancellationToken = default);

    void Discard();
}

/// <summary>One push for one person, as the sender receives it.</summary>
public abstract record PushJob(long RecipientId);

public sealed record NotificationPushJob(long RecipientId, long NotificationId, bool Digest) : PushJob(RecipientId);

public sealed record MessagePushJob(long RecipientId, long MessageId, long? TeamId, bool Mentioned) : PushJob(RecipientId);

public sealed record ReminderPushJob(long RecipientId, string Kind, PushTarget Target, string? ProjectTitle) : PushJob(RecipientId);
