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
    long? PhotoFileId);

public sealed record ChatTeam(long Id, string Name, string Domain);

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

    /// <summary>Up to <paramref name="limit"/> messages older than <paramref name="beforeId"/> (or the newest), newest first. Not tracked.</summary>
    Task<IReadOnlyList<MessageView>> MessagesAsync(long conversationId, long? beforeId, int limit, CancellationToken cancellationToken = default);

    /// <summary>These messages, in no particular order. Not tracked.</summary>
    Task<IReadOnlyList<MessageView>> MessagesByIdAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);

    Task<long?> LastMessageIdAsync(long conversationId, CancellationToken cancellationToken = default);

    /// <summary>The user's read marker in the conversation. Tracked.</summary>
    /// <summary>Moves the person's read marker forward to <paramref name="messageId"/> (never back), creating it if needed.</summary>
    Task MoveReadMarkerAsync(long userId, long conversationId, long messageId, DateTime now, CancellationToken cancellationToken = default);


    /// <summary>
    /// The user's existing conversations: the group chats of <paramref name="teamIds"/> and their direct chats. Unread
    /// counts what others sent after the user's read marker; an unread mention is one of those naming the user.
    /// </summary>
    Task<IReadOnlyList<ConversationState>> StatesAsync(
        long userId,
        IReadOnlyCollection<long> teamIds,
        CancellationToken cancellationToken = default);
}
