using ID_Indetails.api.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ID_Indetails.api.Hubs;

/// <summary>
/// The live connection. Clients only listen, for two signals that carry no content: <c>notifications</c> (the bell
/// changed) and <c>messages</c> (<c>{ teamId }</c> or <c>{ userId }</c>: that chat changed). The client then reloads
/// what changed through the API. Browsers pass the access token as the <c>access_token</c> query parameter.
/// </summary>
[Authorize]
public sealed class LiveHub : Hub
{
    public const string Path = "/hubs/live";
}

/// <summary>Addresses connections by user id, the token's <c>sub</c> claim.</summary>
public sealed class LiveUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.FindFirst(AuthClaimTypes.UserId)?.Value;
}
