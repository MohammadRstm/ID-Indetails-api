using ID_Indetails.api.DTO.Request.Notifications;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.Services.Notifications.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The signed-in person's own notifications. No permission needed: everyone has a bell.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    /// <summary>Newest first.</summary>
    [HttpGet]
    public Task<PagedResponse<NotificationResponse>> List([FromQuery] NotificationListQuery query, CancellationToken cancellationToken)
        => notificationService.ListAsync(query, cancellationToken);

    [HttpGet("unread-count")]
    public Task<UnreadCountResponse> UnreadCount(CancellationToken cancellationToken)
        => notificationService.UnreadCountAsync(cancellationToken);

    [HttpPost("{id:long}/read")]
    public Task<UnreadCountResponse> MarkRead(long id, CancellationToken cancellationToken)
        => notificationService.MarkReadAsync(id, cancellationToken);

    [HttpPost("read-all")]
    public Task<UnreadCountResponse> MarkAllRead(CancellationToken cancellationToken)
        => notificationService.MarkAllReadAsync(cancellationToken);
}
