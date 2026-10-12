using ID_Indetails.api.DTO.Request.Push;
using ID_Indetails.api.DTO.Response.Push;

namespace ID_Indetails.api.Services.Push.Abstraction;

/// <summary>The caller's phones that receive pushes. Each is tied to the phone app's session it was registered from.</summary>
public interface IDeviceService
{
    /// <summary>Registers or updates this phone for the caller's session. A token registered to someone else moves to the caller.</summary>
    Task<DeviceResponse> RegisterAsync(RegisterDeviceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Stops pushes to this phone. Nothing happens for a website session.</summary>
    Task DeleteCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>The caller's phones whose session is still live, newest first.</summary>
    Task<IReadOnlyList<DeviceResponse>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Signs that phone out: its session ends and it gets no more pushes. Someone else's device reads as not found.</summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Deletes the devices whose session has ended, and says how many.</summary>
    Task<int> DeleteDeadAsync(CancellationToken cancellationToken = default);
}
