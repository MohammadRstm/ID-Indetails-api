using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Portal.Abstraction;

/// <summary>A project's client portal discussion: who takes part, the chat, comments, mentions, read markers, and clients' notifications.</summary>
public interface IPortalDiscussionRepository
{
    Task<DiscussionProject?> ProjectAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Active staff taking part: super admins, the project's manager, and whoever uploaded a portal file to it or logged work on it.</summary>
    Task<IReadOnlyList<DiscussionPerson>> StaffAsync(DiscussionProject project, CancellationToken cancellationToken = default);

    /// <summary>Active client accounts with live access to the project.</summary>
    Task<IReadOnlyList<DiscussionPerson>> ClientsAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Up to <paramref name="take"/> messages before <paramref name="before"/> (or the newest), newest first.</summary>
    Task<IReadOnlyList<DiscussionMessageRow>> MessagesAsync(long projectId, long? before, int take, CancellationToken cancellationToken = default);

    Task<DiscussionMessageRow?> MessageRowAsync(long projectId, long messageId, CancellationToken cancellationToken = default);

    /// <summary>Tracked.</summary>
    Task<PortalMessage?> GetMessageAsync(long projectId, long messageId, CancellationToken cancellationToken = default);

    /// <summary>The writer's message sent with this token, if any. Tracked.</summary>
    Task<PortalMessage?> FindMessageAsync(DiscussionPerson author, string clientToken, CancellationToken cancellationToken = default);

    Task<long?> NewestMessageIdAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>The newest comment on a file of the project, deleted ones included.</summary>
    Task<long?> NewestCommentIdAsync(long projectId, long itemId, CancellationToken cancellationToken = default);

    /// <summary>Messages after <paramref name="after"/> not written by <paramref name="reader"/>.</summary>
    Task<int> UnreadMessagesAsync(long projectId, long? after, DiscussionPerson reader, CancellationToken cancellationToken = default);

    /// <summary>A file's comments (or every comment of the project), oldest first, deleted ones included.</summary>
    Task<IReadOnlyList<DiscussionCommentRow>> CommentsAsync(long projectId, long? itemId, CancellationToken cancellationToken = default);

    Task<DiscussionCommentRow?> CommentRowAsync(long projectId, long commentId, CancellationToken cancellationToken = default);

    /// <summary>Tracked.</summary>
    Task<PortalComment?> GetCommentAsync(long projectId, long commentId, CancellationToken cancellationToken = default);

    /// <summary>The writer's comment sent with this token, if any. Tracked.</summary>
    Task<PortalComment?> FindCommentAsync(DiscussionPerson author, string clientToken, CancellationToken cancellationToken = default);

    /// <summary>The project's files clients see now (removed ones too, until the next publish), by item.</summary>
    Task<IReadOnlyDictionary<long, PublishedItemFacts>> PublishedItemsAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>The mentioned people's keys, by message.</summary>
    Task<ILookup<long, string>> MessageMentionsAsync(IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default);

    /// <summary>The mentioned people's keys, by comment.</summary>
    Task<ILookup<long, string>> CommentMentionsAsync(IReadOnlyCollection<long> commentIds, CancellationToken cancellationToken = default);

    /// <summary>A message's or a comment's mentions. Tracked.</summary>
    Task<IReadOnlyList<PortalMention>> MentionsAsync(long? messageId, long? commentId, CancellationToken cancellationToken = default);

    /// <summary>The reader's marker on the project's chat, if any. Tracked.</summary>
    Task<PortalRead?> GetReadAsync(long projectId, DiscussionPerson reader, CancellationToken cancellationToken = default);

    /// <summary>The reader's marker on a file's comments, if any. Tracked.</summary>
    Task<PortalCommentRead?> GetCommentReadAsync(long projectId, long itemId, DiscussionPerson reader, CancellationToken cancellationToken = default);

    /// <summary>How far the reader has read each file's comments, by item.</summary>
    Task<IReadOnlyDictionary<long, long>> CommentReadsAsync(long projectId, DiscussionPerson reader, CancellationToken cancellationToken = default);

    /// <summary>Unread chat messages and comments on each project, for a client.</summary>
    Task<IReadOnlyDictionary<long, int>> UnreadForClientAsync(long accountId, IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>The client's notification with this key, locked until the transaction ends. Tracked.</summary>
    Task<PortalNotification?> GetNotificationForUpdateAsync(long accountId, string digestKey, CancellationToken cancellationToken = default);

    /// <summary>A client's notifications on projects they still have access to, newest first.</summary>
    Task<IReadOnlyList<PortalNotificationRow>> NotificationsAsync(long accountId, long? before, int take, CancellationToken cancellationToken = default);

    Task<int> UnreadNotificationsAsync(long accountId, CancellationToken cancellationToken = default);

    /// <summary>Marks the client's notifications read: these, or all of them.</summary>
    Task MarkNotificationsReadAsync(long accountId, IReadOnlyCollection<long>? ids, DateTime now, CancellationToken cancellationToken = default);

    void Add(PortalMessage message);

    void Add(PortalComment comment);

    void Add(PortalMention mention);

    void Add(PortalRead read);

    void Add(PortalCommentRead read);

    void Add(PortalNotification notification);

    void Remove(PortalMention mention);
}
