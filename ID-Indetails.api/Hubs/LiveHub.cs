using ID_Indetails.api.Constants;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ID_Indetails.api.Hubs;

/// <summary>
/// The live connection. Clients only listen, for two signals that carry no content: <c>notifications</c> (the bell
/// changed) and <c>messages</c> (<c>{ teamId }</c> or <c>{ userId }</c>: that chat changed). The client then reloads
/// what changed through the API. Browsers pass the access token as the <c>access_token</c> query parameter.
/// An open connection is what makes someone online (<see cref="IPresence"/>).
/// </summary>
[Authorize]
public sealed class LiveHub(IPresence presence, IUserRepository users, IClock clock) : Hub
{
    public const string Path = "/hubs/live";

    public override Task OnConnectedAsync()
    {
        if (UserId is { } id)
        {
            presence.Connected(id);
        }

        return base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Closing their last connection is when they were last seen.
        if (UserId is { } id && presence.Disconnected(id))
        {
            await users.SetLastSeenAsync(id, clock.UtcNow);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private long? UserId => long.TryParse(Context.UserIdentifier, out var id) ? id : null;
}

/// <summary>Addresses connections by user id, the token's <c>sub</c> claim.</summary>
public sealed class LiveUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.FindFirst(AuthClaimTypes.UserId)?.Value;
}
