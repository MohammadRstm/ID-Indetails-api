using System.Text.Json;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.api.Services.Notifications.Implementation;

public sealed class NotificationSender(
    INotificationRepository notifications,
    IUserRepository users,
    IPermissionService permissions,
    ILiveUpdates live,
    IClock clock) : INotificationSender
{
    public async Task SendAsync(NotificationDraft draft, IEnumerable<long> recipientIds, CancellationToken cancellationToken = default)
    {
        var payload = Serialise(draft);
        var allowed = await AllowedAsync(draft, recipientIds, cancellationToken);
        foreach (var recipientId in allowed)
        {
            notifications.Add(New(draft, recipientId, payload));
        }

        live.NotificationsChanged(allowed);
    }

    public async Task SendDigestAsync(
        NotificationDraft draft,
        string digestKey,
        IEnumerable<long> recipientIds,
        CancellationToken cancellationToken = default)
    {
        var payload = Serialise(draft);
        var allowed = await AllowedAsync(draft, recipientIds, cancellationToken);
        live.NotificationsChanged(allowed);
        foreach (var recipientId in allowed)
        {
            var digest = await notifications.GetDigestForUpdateAsync(recipientId, digestKey, cancellationToken);
            if (digest is null)
            {
                var created = New(draft, recipientId, payload);
                created.DigestKey = digestKey;
                notifications.Add(created);
                continue;
            }

            digest.Count++;
            digest.ActorId = draft.ActorId;
            digest.SubjectType = draft.SubjectType;
            digest.SubjectId = draft.SubjectId;
            digest.Payload = payload;
            digest.CreatedAt = clock.UtcNow;
            digest.ReadAt = null;
        }
    }

    public async Task SendOnceAsync(NotificationDraft draft, string key, IEnumerable<long> recipientIds, CancellationToken cancellationToken = default)
    {
        var allowed = await AllowedAsync(draft, recipientIds, cancellationToken);
        if (allowed.Count == 0)
        {
            return;
        }

        var payload = Serialise(draft);
        var alreadySent = await notifications.RecipientsWithKeyAsync(key, allowed, cancellationToken);
        var fresh = allowed.Where(id => !alreadySent.Contains(id)).ToList();
        foreach (var recipientId in fresh)
        {
            var created = New(draft, recipientId, payload);
            created.DigestKey = key;
            notifications.Add(created);
        }

        live.NotificationsChanged(fresh);
    }

    public Task<IReadOnlyList<long>> DomainMembersAsync(string domain, CancellationToken cancellationToken = default)
        => users.ActiveIdsInDomainAsync(domain, cancellationToken);

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

    private async Task<IReadOnlyList<long>> AllowedAsync(NotificationDraft draft, IEnumerable<long> recipientIds, CancellationToken cancellationToken)
    {
        var allowed = new List<long>();
        foreach (var recipientId in recipientIds.Distinct())
        {
            // A deactivated account has no access at all, so it can't see any domain; company-level (null) needs one too.
            var access = await permissions.GetAccessAsync(recipientId, cancellationToken);
            if (recipientId != draft.ActorId && access.VisibleDomains.Count > 0 && access.CanSee(draft.DomainCode))
            {
                allowed.Add(recipientId);
            }
        }

        return allowed;
    }

    private static string? Serialise(NotificationDraft draft)
        => draft.Payload is null ? null : JsonSerializer.Serialize(draft.Payload, JsonSerializerOptions.Web);

    private Notification New(NotificationDraft draft, long recipientId, string? payload) => new()
    {
        RecipientId = recipientId,
        Kind = draft.Kind,
        ActorId = draft.ActorId,
        ProjectId = draft.ProjectId,
        SubjectType = draft.SubjectType,
        SubjectId = draft.SubjectId,
        Payload = payload,
        CreatedAt = clock.UtcNow,
    };
}
