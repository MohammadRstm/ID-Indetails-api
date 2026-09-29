using ID_Indetails.api.DTO.Request.Notifications;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;

namespace ID_Indetails.api.Services.Notifications.Abstraction;

/// <summary>The signed-in person's own notifications.</summary>
public interface INotificationService
{
    Task<PagedResponse<NotificationResponse>> ListAsync(NotificationListQuery query, CancellationToken cancellationToken = default);

    Task<UnreadCountResponse> UnreadCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the unread count left, for the badge.</summary>
    Task<UnreadCountResponse> MarkReadAsync(long id, CancellationToken cancellationToken = default);

    Task<UnreadCountResponse> MarkAllReadAsync(CancellationToken cancellationToken = default);
}
