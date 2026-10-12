using ID_Indetails.api.DTO.Request.Messaging;
using ID_Indetails.api.DTO.Response.Messaging;

namespace ID_Indetails.api.Services.Messaging.Abstraction;

/// <summary>Team chats and direct messages (see <c>Business/Messaging/MessagingRules</c>).</summary>
public interface IMessagingService
{
    /// <summary>Every team chat the caller is in, and their direct chats with messages; most recent first.</summary>
    Task<IReadOnlyList<ConversationResponse>> ConversationsAsync(CancellationToken cancellationToken = default);

    Task<MessagesBadgeResponse> BadgeAsync(CancellationToken cancellationToken = default);

    /// <summary>Someone's unread messages, counted as their badge counts them (for the app icon's badge on a push). 0 for anyone inactive.</summary>
    Task<int> UnreadCountForAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>The people the caller can message directly, by name.</summary>
    Task<IReadOnlyList<ContactResponse>> ContactsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactResponse>> TeamMembersAsync(long teamId, CancellationToken cancellationToken = default);

    Task<MessagePageResponse> TeamMessagesAsync(long teamId, MessagePageQuery query, CancellationToken cancellationToken = default);

    Task<MessageResponse> SendToTeamAsync(long teamId, SendMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Marks the chat read up to its latest message and returns the new badge.</summary>
    Task<MessagesBadgeResponse> MarkTeamReadAsync(long teamId, CancellationToken cancellationToken = default);

    Task<MessagePageResponse> DirectMessagesAsync(long userId, MessagePageQuery query, CancellationToken cancellationToken = default);

    Task<MessageResponse> SendDirectAsync(long userId, SendMessageRequest request, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="MarkTeamReadAsync"/>
    Task<MessagesBadgeResponse> MarkDirectReadAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Whether the caller may read this conversation now: a team chat they are in, or their direct chat with someone they can reach.</summary>
    Task<bool> CanReadConversationAsync(long conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a work-entry comment to the entry author's team chat as a card mentioning everyone in it, and returns the
    /// message id. Runs in the caller's transaction. The author must be in a team of the entry's domain, and the
    /// commenter in that chat.
    /// </summary>
    Task<long> PostTaskCommentAsync(
        long commenterId,
        long entryAuthorId,
        string entryDomain,
        long entryId,
        long commentId,
        string body,
        CancellationToken cancellationToken = default);
}
