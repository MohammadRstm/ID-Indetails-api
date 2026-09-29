using System.Text.Json;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;

namespace ID_Indetails.api.Services.Notifications.Implementation;

public sealed class NotificationSender(
    INotificationRepository notifications,
    IPermissionService permissions,
    IClock clock) : INotificationSender
{
    public async Task SendAsync(NotificationDraft draft, IEnumerable<long> recipientIds, CancellationToken cancellationToken = default)
    {
        var payload = draft.Payload is null ? null : JsonSerializer.Serialize(draft.Payload, JsonSerializerOptions.Web);
        foreach (var recipientId in recipientIds.Distinct())
        {
            // A deactivated account has no access at all, so it can't see the domain either.
            if (recipientId == draft.ActorId || !(await permissions.GetAccessAsync(recipientId, cancellationToken)).CanSee(draft.DomainCode))
            {
                continue;
            }

            notifications.Add(new Notification
            {
                RecipientId = recipientId,
                Kind = draft.Kind,
                ActorId = draft.ActorId,
                ProjectId = draft.ProjectId,
                SubjectType = draft.SubjectType,
                SubjectId = draft.SubjectId,
                Payload = payload,
                CreatedAt = clock.UtcNow,
            });
        }
    }

    public async Task<IReadOnlyList<long>> HoldersAsync(string domain, string permission, CancellationToken cancellationToken = default)
    {
        var holders = new List<long>();
        foreach (var id in await notifications.PeopleWhoMayHoldAsync(domain, permission, cancellationToken))
        {
            var access = await permissions.GetAccessAsync(id, cancellationToken);
            if (access.Has(permission) && access.CanSee(domain))
            {
                holders.Add(id);
            }
        }

        return holders;
    }
}
