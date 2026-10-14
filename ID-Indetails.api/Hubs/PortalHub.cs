using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ID_Indetails.api.Hubs;

/// <summary>
/// The client portal's live connection, apart from the staff one (<see cref="LiveHub"/>): only portal tokens open it,
/// and each connection joins its account's group, so a client id can never reach a staff member with the same id.
/// Clients only listen, for signals that carry no content: <c>portal</c> (<c>{ projectId }</c>: that project's
/// discussion changed) and <c>notifications</c> (the bell changed). Browsers pass the token as <c>access_token</c>.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Portal)]
public sealed class PortalHub : Hub
{
    public const string Path = "/hubs/portal";

    public static string Group(long accountId) => $"account:{accountId}";

    public override async Task OnConnectedAsync()
    {
        if (long.TryParse(Context.User?.FindFirst(AuthClaimTypes.PortalAccountId)?.Value, out var accountId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(accountId));
        }

        await base.OnConnectedAsync();
    }
}
