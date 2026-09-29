using ID_Indetails.api.DTO.Response.Realtime;
using ID_Indetails.api.Services.Realtime.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Who is online, for the dots on avatars and the "online now" count.</summary>
[ApiController]
[Route("api/presence")]
[Authorize]
public sealed class PresenceController(IPresenceService presenceService) : ControllerBase
{
    [HttpGet]
    public Task<PresenceResponse> Online(CancellationToken cancellationToken) => presenceService.OnlineAsync(cancellationToken);
}
