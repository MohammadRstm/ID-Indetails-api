using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Realtime;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.api.Services.Realtime.Implementation;

public sealed class PresenceService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IUserRepository users,
    IPresence presence) : IPresenceService
{
    public async Task<PresenceResponse> OnlineAsync(CancellationToken cancellationToken = default)
    {
        // An access token outlives a deactivation.
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        if (actor.Permissions.Count == 0)
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You don't have permission to do this.");
        }

        var online = presence.OnlineUserIds();
        if (online.Count == 0)
        {
            return new PresenceResponse([]);
        }

        var people = await users.ActiveDomainsAsync(online, cancellationToken);
        return new PresenceResponse(people.Where(p => actor.CanSee(p.DomainCode)).Select(p => p.Id).Order().ToList());
    }
}
