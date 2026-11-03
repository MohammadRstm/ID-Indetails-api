using ID_Indetails.api.DTO.Request.Push;
using ID_Indetails.api.DTO.Response.Push;

namespace ID_Indetails.api.Services.Push.Abstraction;

/// <summary>
/// The caller's browser that receives pushes (Web Push). Like a phone, it is tied to its session, which the refresh
/// cookie names, so a session that ends takes its pushes with it.
/// </summary>
public interface IWebPushService
{
    WebPushKeyResponse Key();

    /// <summary>
    /// Registers or updates this browser for the caller's session. A subscription registered to someone else moves to the
    /// caller; the session's earlier one is removed.
    /// </summary>
    Task RegisterAsync(string? refreshCookieValue, RegisterWebPushRequest request, CancellationToken cancellationToken = default);

    /// <summary>Stops pushes to this browser. Nothing happens without a session.</summary>
    Task DeleteAsync(string? refreshCookieValue, CancellationToken cancellationToken = default);
}
