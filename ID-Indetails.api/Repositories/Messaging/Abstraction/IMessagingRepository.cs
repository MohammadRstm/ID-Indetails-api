using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Files.Abstraction;

namespace ID_Indetails.api.Repositories.Messaging.Abstraction;

/// <summary>Someone who may take part in chats, with what the rules and the contact list need.</summary>
public sealed record ChatPerson(
    long Id,
    string Name,
    string Role,
    string? Domain,
    long? TeamId,
    string? TeamName,
    string? Position,
    UserStatus Status,
    long? PhotoFileId,
    DateTime CreatedAt = default,
    DateTime? LastSeenAt = null);

public sealed record ChatTeam(long Id, string Name, string Domain);

/// <summary>A custom chat group, with everyone stored in it (active or not).</summary>
public sealed record ChatGroup(long Id, string Name, long? PhotoFileId, IReadOnlyList<long> MemberIds, DateTime CreatedAt);

public sealed record MessagePerson(long Id, string Name, long? PhotoFileId = null);

public sealed record MessageView(
    long Id,
    long ConversationId,
    MessagePerson Sender,
    string? Body,
    string Kind,
    long? RefEntryId,
    long? RefCommentId,
    IReadOnlyList<MessagePerson> Mentions,
    IReadOnlyList<FileView> Attachments,
    DateTime CreatedAt,
    bool ViaProton);

/// <summary>An existing conversation of the user, with where they read up to and what is still unread.</summary>
public sealed record ConversationState(
    long Id,
    ConversationType Type,
    long? TeamId,
    long? OtherUserId,
    long? LastMessageId,
    int Unread,
    bool UnreadMention);

/// <summary>A conversation of the user with something unread, as <see cref="ConversationState"/> counts it.</summary>
public sealed record UnreadChat(long Id, ConversationType Type, long? TeamId, long? OtherUserId, int Unread, bool UnreadMention);

/// <summary>Someone's read and received markers in a conversation.</summary>
/// <param name="ReadMovedAt">When the read marker last moved.</param>
public sealed record ReadMarker(long ConversationId, long UserId, long? Read, long? Delivered, DateTime ReadMovedAt);

/// <summary>A marker that moved forward in a conversation, from the message it was at (0 for none) to another.</summary>
public sealed record MarkerMove(long ConversationId, long From, long To);

public interface IMessagingRepository
{
    /// <summary>Everyone, active or not, by id. Not tracked.</summary>
    Task<IReadOnlyList<ChatPerson>> PeopleAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);

    /// <summary>Every active person, by name. Not tracked. The rules decide who of them the caller may reach.</summary>
    Task<IReadOnlyList<ChatPerson>> ActivePeopleAsync(CancellationToken cancellationToken = default);

    /// <summary>Every team, by domain then name. Not tracked.</summary>
    Task<IReadOnlyList<ChatTeam>> TeamsAsync(CancellationToken cancellationToken = default);

    /// <summary>A conversation by id. Not tracked.</summary>
    Task<Conversation?> ConversationAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The team's group chat, if anyone wrote in it yet. Tracked.</summary>
    Task<Conversation?> TeamConversationAsync(long teamId, CancellationToken cancellationToken = default);

    /// <summary>The Announcements chat, which the migration that added it created. Tracked.</summary>
    Task<Conversation?> AnnouncementsAsync(CancellationToken cancellationToken = default);

    /// <summary>The two people's direct chat, if anyone wrote in it yet. Tracked.</summary>
    Task<Conversation?> DirectConversationAsync(long userA, long userB, CancellationToken cancellationToken = default);

    /// <summary>
    /// For writing: locks the team until the transaction ends, then reads its chat with a locking read, which sees the
    /// latest committed rows. So two first messages at once create one chat. Tracked.
    /// </summary>
    Task<Conversation?> TeamConversationForUpdateAsync(long teamId, CancellationToken cancellationToken = default);

    /// <summary>The same for a direct chat, locking the first of the two people.</summary>
    Task<Conversation?> DirectConversationForUpdateAsync(long userA, long userB, CancellationToken cancellationToken = default);

    void Add(Conversation conversation);

    void Add(Message message);

    /// <summary>Every custom group, by name. Not tracked.</summary>
    Task<IReadOnlyList<ChatGroup>> GroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>The custom groups the user is in, by name. Not tracked.</summary>
    Task<IReadOnlyList<ChatGroup>> GroupsOfAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>A custom group by its conversation id. Not tracked.</summary>
    Task<ChatGroup?> GroupAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// For changing a custom group: locks its row until the transaction ends, so changes to one group happen one at a
    /// time. Tracked, with its members.
    /// </summary>
    Task<Conversation?> GroupForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The files a custom group holds: its messages' attachments and its photo. Tracked, for deleting them.</summary>
    Task<IReadOnlyList<StoredFile>> GroupFilesAsync(long conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a custom group with everything in it: its messages (their mentions and attachments go with them), read
    /// markers, read times and members. Its files are the caller's to remove. Saved with the unit of work.
    /// </summary>
    Task RemoveGroupAsync(Conversation group, CancellationToken cancellationToken = default);

    /// <summary>The message with its attachments and their files, for deleting it. Tracked.</summary>
    Task<Message?> MessageForDeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the message (its mentions and attachments go with it). Read and received markers on it move back to the
    /// chat's message before it, and its Announcements read times are dropped. Saved with the unit of work.
    /// </summary>
    Task RemoveAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>Up to <paramref name="limit"/> messages older than <paramref name="beforeId"/> (or the newest), newest first. Not tracked.</summary>
    Task<IReadOnlyList<MessageView>> MessagesAsync(long conversationId, long? beforeId, int limit, CancellationToken cancellationToken = default);

    /// <summary>These messages, in no particular order. Not tracked.</summary>
    Task<IReadOnlyList<MessageView>> MessagesByIdAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);

    Task<long?> LastMessageIdAsync(long conversationId, CancellationToken cancellationToken = default);

    /// <summary>Moves the person's read marker forward to <paramref name="messageId"/> (never back), creating it if needed.</summary>
    Task MoveReadMarkerAsync(long userId, long conversationId, long messageId, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>The message the person has read up to in the conversation, if any.</summary>
    Task<long?> ReadUpToAsync(long userId, long conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the person's received marker in these conversations forward to the newest message others sent there, where
    /// neither of their markers is there yet. Returns the moves, from the further of their two markers.
    /// </summary>
    Task<IReadOnlyList<MarkerMove>> MoveDeliveredAsync(
        long userId,
        IReadOnlyCollection<long> conversationIds,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>Everyone's markers in these conversations. Not tracked.</summary>
    Task<IReadOnlyList<ReadMarker>> MarkersAsync(IReadOnlyCollection<long> conversationIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the person read, now, the messages after <paramref name="afterId"/> up to <paramref name="upToId"/>
    /// that others sent since <paramref name="joined"/>. For Announcements; a message already recorded keeps its time.
    /// </summary>
    Task RecordReadsAsync(
        long userId,
        long conversationId,
        long afterId,
        long upToId,
        DateTime joined,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>When each person read the message, where recorded (Announcements only).</summary>
    Task<IReadOnlyDictionary<long, DateTime>> ReadTimesAsync(long messageId, CancellationToken cancellationToken = default);

    /// <summary>Who other than <paramref name="exceptUserId"/> sent the messages after <paramref name="afterId"/> up to <paramref name="upToId"/>.</summary>
    Task<IReadOnlyList<long>> SendersBetweenAsync(
        long conversationId,
        long afterId,
        long upToId,
        long exceptUserId,
        CancellationToken cancellationToken = default);


    /// <summary>
    /// The user's existing conversations: Announcements, the group chats of <paramref name="teamIds"/> and their direct
    /// chats. Unread counts what others sent after the user's read marker, and in Announcements only since the user
    /// joined; an unread mention is one of those naming the user.
    /// </summary>
    Task<IReadOnlyList<ConversationState>> StatesAsync(
        long userId,
        IReadOnlyCollection<long> teamIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same conversations' unread counts and mentions, only those with something unread, for the badge: without
    /// the last messages, and without looking for mentions when nothing is unread.
    /// </summary>
    Task<IReadOnlyList<UnreadChat>> UnreadChatsAsync(
        long userId,
        IReadOnlyCollection<long> teamIds,
        CancellationToken cancellationToken = default);
}
