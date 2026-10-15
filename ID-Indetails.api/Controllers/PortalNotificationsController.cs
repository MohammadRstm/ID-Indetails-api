using ID_Indetails.api.Authorization;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The signed-in client's bell: only projects they still have access to.</summary>
[ApiController]
[Route("api/portal/notifications")]
[Authorize(Policy = AuthorizationPolicies.Portal)]
public sealed class PortalNotificationsController(IPortalNotificationService notifications) : ControllerBase
{
    [HttpGet]
    public Task<PortalNotificationPageResponse> List([FromQuery] long? before, [FromQuery] int? limit, CancellationToken cancellationToken)
        => notifications.ListAsync(before, limit, cancellationToken);

    [HttpGet("unread-count")]
    public Task<PortalUnreadResponse> Unread(CancellationToken cancellationToken)
        => notifications.UnreadAsync(cancellationToken);

    [HttpPost("read")]
    public Task<PortalUnreadResponse> Read(PortalNotificationsReadRequest request, CancellationToken cancellationToken)
        => notifications.MarkReadAsync(request, cancellationToken);
}
