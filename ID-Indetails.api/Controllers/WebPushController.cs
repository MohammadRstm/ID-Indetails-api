using ID_Indetails.api.Authorization;
using ID_Indetails.api.DTO.Request.Push;
using ID_Indetails.api.DTO.Response.Push;
using ID_Indetails.api.Services.Push.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// This browser's pushes (Web Push). Under <c>/api/auth</c> because the refresh cookie, sent only there, names the
/// browser's session, which its pushes end with. Signed in only; nobody registers or stops anyone else's.
/// </summary>
[ApiController]
[Route("api/auth/web-push")]
[Authorize]
public sealed class WebPushController(IWebPushService webPush, RefreshTokenCookie refreshCookie) : ControllerBase
{
    /// <summary>The key a browser subscribes with. Null while browsers get no pushes: the app then doesn't offer them.</summary>
    [HttpGet]
    public WebPushKeyResponse Key() => webPush.Key();

    /// <summary>Registers this browser for pushes, for as long as its session lasts.</summary>
    [HttpPut]
    public async Task<IActionResult> Register(RegisterWebPushRequest request, CancellationToken cancellationToken)
    {
        await webPush.RegisterAsync(refreshCookie.Read(Request), request, cancellationToken);
        return NoContent();
    }

    /// <summary>Stops pushes to this browser.</summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken cancellationToken)
    {
        await webPush.DeleteAsync(refreshCookie.Read(Request), cancellationToken);
        return NoContent();
    }
}
