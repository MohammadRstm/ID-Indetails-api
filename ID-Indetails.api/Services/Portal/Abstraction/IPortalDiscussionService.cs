using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>
/// A Design client project's discussion, the same for both sides: the project chat and comments on published files,
/// with mentions, edits and read markers. <c>side</c> says whether the caller is signed in as staff or as a
/// client; whether they take part is worked out on every call, and anyone who doesn't gets a 404.
/// </summary>
public interface IPortalDiscussionService
{
    Task<DiscussionSummaryResponse> SummaryAsync(DiscussionSide side, long projectId, CancellationToken cancellationToken = default);

    Task<PortalMessagePageResponse> MessagesAsync(DiscussionSide side, long projectId, long? before, int? limit, CancellationToken cancellationToken = default);

    /// <summary>Sends a message. The same client token again answers with the first message and notifies nobody twice.</summary>
    Task<PortalChatMessageResponse> SendMessageAsync(DiscussionSide side, long projectId, PortalMessageRequest request, CancellationToken cancellationToken = default);

    Task<PortalChatMessageResponse> EditMessageAsync(
        DiscussionSide side, long projectId, long messageId, PortalEditRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes one's own message for everyone: it keeps its place without its text or mentions. Safe to repeat.</summary>
    Task<PortalChatMessageResponse> DeleteMessageAsync(DiscussionSide side, long projectId, long messageId, CancellationToken cancellationToken = default);

    Task<PortalCommentThreadResponse> ThreadAsync(DiscussionSide side, long projectId, long itemId, CancellationToken cancellationToken = default);

    Task<PortalCommentResponse> AddCommentAsync(DiscussionSide side, long projectId, PortalCommentRequest request, CancellationToken cancellationToken = default);

    Task<PortalCommentResponse> EditCommentAsync(
        DiscussionSide side, long projectId, long commentId, PortalEditRequest request, CancellationToken cancellationToken = default);

    /// <summary>Takes one's own comment's text away; the comment keeps its place as deleted.</summary>
    Task<PortalCommentResponse> DeleteCommentAsync(DiscussionSide side, long projectId, long commentId, CancellationToken cancellationToken = default);

    Task<DiscussionSummaryResponse> MarkReadAsync(DiscussionSide side, long projectId, PortalReadRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A signed-in client's notifications, only from projects they still have access to.</summary>
public interface IPortalNotificationService
{
    Task<PortalNotificationPageResponse> ListAsync(long? before, int? limit, CancellationToken cancellationToken = default);

    Task<PortalUnreadResponse> UnreadAsync(CancellationToken cancellationToken = default);

    Task<PortalUnreadResponse> MarkReadAsync(PortalNotificationsReadRequest request, CancellationToken cancellationToken = default);
}
