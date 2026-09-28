using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;

namespace ID_Indetails.api.Services.Notifications.Implementation;

public sealed class NotificationRecipients(IUserRepository users, IPermissionService permissions) : INotificationRecipients
{
    public async Task<IReadOnlyList<long>> HoldersAsync(
        string domain,
        string permission,
        long exceptUserId,
        CancellationToken cancellationToken = default)
    {
        var holders = new List<long>();
        foreach (var id in await users.ActiveIdsInDomainAsync(domain, cancellationToken))
        {
            if (id != exceptUserId && (await permissions.GetAccessAsync(id, cancellationToken)).Has(permission))
            {
                holders.Add(id);
            }
        }

        return holders;
    }
}
