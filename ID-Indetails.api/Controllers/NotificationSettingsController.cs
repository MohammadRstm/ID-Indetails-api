using ID_Indetails.api.DTO.Request.Notifications;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.Services.Notifications.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The caller's own quiet hours and muted push categories. Signed in only.</summary>
[ApiController]
[Route("api/me/notification-settings")]
[Authorize]
public sealed class NotificationSettingsController(INotificationSettingsService settings) : ControllerBase
{
    [HttpGet]
    public Task<NotificationSettingsResponse> Get(CancellationToken cancellationToken)
        => settings.GetAsync(cancellationToken);

    /// <summary>Replaces the caller's settings.</summary>
    [HttpPut]
    public Task<NotificationSettingsResponse> Save(SaveNotificationSettingsRequest request, CancellationToken cancellationToken)
        => settings.SaveAsync(request, cancellationToken);
}
