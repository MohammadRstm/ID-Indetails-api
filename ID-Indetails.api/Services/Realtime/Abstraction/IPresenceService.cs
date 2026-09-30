using ID_Indetails.api.DTO.Response.Realtime;

namespace ID_Indetails.api.Services.Realtime.Abstraction;

public interface IPresenceService
{
    /// <summary>Who is online among the people of the domains the caller can see, super admins included.</summary>
    Task<PresenceResponse> OnlineAsync(CancellationToken cancellationToken = default);
}
