using ID_Indetails.api.DTO.Request.Push;
using ID_Indetails.api.DTO.Response.Push;
using ID_Indetails.api.Services.Push.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The caller's own phones that receive pushes. Signed in only; nobody sees or removes anyone else's.</summary>
[ApiController]
[Route("api/me/devices")]
[Authorize]
public sealed class DevicesController(IDeviceService devices) : ControllerBase
{
    /// <summary>Registers this phone for pushes. Needs the phone app's sign-in (its session id).</summary>
    [HttpPut]
    public Task<DeviceResponse> Register(RegisterDeviceRequest request, CancellationToken cancellationToken)
        => devices.RegisterAsync(request, cancellationToken);

    /// <summary>Stops pushes to this phone.</summary>
    [HttpDelete("current")]
    public async Task<IActionResult> DeleteCurrent(CancellationToken cancellationToken)
    {
        await devices.DeleteCurrentAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Newest first.</summary>
    [HttpGet]
    public Task<IReadOnlyList<DeviceResponse>> List(CancellationToken cancellationToken)
        => devices.ListAsync(cancellationToken);

    /// <summary>Signs that phone out and stops its pushes.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        await devices.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
