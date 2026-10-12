using System.Text.Json;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Notifications;
using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Messaging.Abstraction;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using ID_Indetails.api.Repositories.Push.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Messaging.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Push.Abstraction;

namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>
/// Everything is decided at send time, not when the push was queued: only phones whose session still has a valid
/// refresh token receive anything, a deactivated person gets nothing, and a notification goes out only while its
/// reader may still read it (the same check as the in-app list: <see cref="NotificationAccess"/> and the domains they
/// see), unless the person muted its category. During their quiet hours a push is delivered silently on the
/// <c>quiet</c> channel.
/// </summary>
public sealed class PushDelivery(
    IUserDeviceRepository devices,
    IPermissionService permissions,
    INotificationRepository notifications,
    IMessagingRepository messaging,
    IMessagingService messages,
    INotificationSettingsService settings,
    IPushProvider provider,
    PushThrottle throttle,
    PushReceipts receipts,
    IClock clock,
    ILogger<PushDelivery> logger) : IPushDelivery
{
    /// <summary>Expo has a receipt ready within about 15 minutes of a send.</summary>
    public static readonly TimeSpan ReceiptDelay = TimeSpan.FromMinutes(15);

    private sealed record Rendered(PushMessageText Text, PushData Data);

    private IReadOnlyList<ChatTeam>? _teams;

    public async Task DeliverAsync(IReadOnlyList<PushJob> jobs, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var phones = (await devices.ListLiveForUsersAsync(jobs.Select(j => j.RecipientId).Distinct().ToList(), now, cancellationToken))
            .ToLookup(d => d.UserId);
        var companyTime = TimeOnly.FromDateTime(clock.ToCompanyTime(now));

        var outgoing = new List<(UserDevice Phone, PushMessage Message)>();
        foreach (var forPerson in jobs.GroupBy(j => j.RecipientId))
        {
            var personPhones = phones[forPerson.Key].ToList();
            if (personPhones.Count == 0)
            {
                continue;
            }

            // a deactivated account has no access at all
            var access = await permissions.GetAccessAsync(forPerson.Key, cancellationToken);
            if (access.VisibleDomains.Count == 0)
            {
                continue;
            }

            var preferences = await settings.PreferencesOfAsync(forPerson.Key, cancellationToken);
            var quiet = preferences.QuietAt(companyTime);
            int? badge = null;
            foreach (var job in forPerson)
            {
                if (await RenderAsync(job, access, cancellationToken) is not { } push || preferences.Mutes(push.Data.Kind))
                {
                    continue;
                }

                badge ??= await BadgeAsync(access, cancellationToken);
                var full = job is not NotificationPushJob { Digest: true } || throttle.TakeFull(forPerson.Key, push.Data.Kind, now);
                outgoing.AddRange(personPhones.Select(phone => (phone, Build(phone, push, badge.Value, quiet, full))));
            }
        }

        if (outgoing.Count == 0)
        {
            return;
        }

        var tickets = await provider.SendAsync(outgoing.Select(o => o.Message).ToList(), cancellationToken);
        for (var i = 0; i < outgoing.Count && i < tickets.Count; i++)
        {
            var phone = outgoing[i].Phone;
            switch (tickets[i])
            {
                case { Error: IPushProvider.DeviceNotRegistered }:
                    await devices.DeleteByTokenAsync(phone.PushToken, cancellationToken);
                    break;
                case { Id: { } ticketId }:
                    receipts.Add(ticketId, phone.PushToken, now);
                    break;
                case { Error: { } error }:
                    logger.LogWarning("A push to device {DeviceId} was refused: {Error}.", phone.Id, error);
                    break;
            }
        }
    }

    public async Task CheckReceiptsAsync(CancellationToken cancellationToken = default)
    {
        var due = receipts.TakeSentBefore(clock.UtcNow - ReceiptDelay);
        if (due.Count == 0)
        {
            return;
        }

        var errors = await provider.ReceiptErrorsAsync(due.Select(d => d.TicketId).ToList(), cancellationToken);
        foreach (var (ticketId, pushToken) in due)
        {
            if (errors.TryGetValue(ticketId, out var error) && error == IPushProvider.DeviceNotRegistered)
            {
                await devices.DeleteByTokenAsync(pushToken, cancellationToken);
            }
        }
    }

    private async Task<Rendered?> RenderAsync(PushJob job, UserAccess access, CancellationToken cancellationToken)
    {
        switch (job)
        {
            case NotificationPushJob n:
                var view = await notifications.GetVisibleAsync(
                    n.NotificationId, n.RecipientId, access.VisibleDomains, NotificationAccess.HiddenKinds(access), cancellationToken);
                if (view is null || view.ReadAt is not null)
                {
                    return null;
                }

                JsonElement? payload = view.Payload is null ? null : JsonDocument.Parse(view.Payload).RootElement.Clone();
                return new(
                    PushText.ForNotification(view.Kind, view.ActorName, view.ProjectTitle, payload, view.Count),
                    new PushData(n.RecipientId, view.Kind, view.Id,
                        PushTarget.ForNotification(view.Kind, view.ProjectId, view.ProjectDomain, view.SubjectType, view.SubjectId, payload)));

            case MessagePushJob m:
                var message = (await messaging.MessagesByIdAsync([m.MessageId], cancellationToken)).SingleOrDefault();
                string? teamName = null;
                if (m.TeamId is { } teamId)
                {
                    _teams ??= await messaging.TeamsAsync(cancellationToken);
                    teamName = _teams.SingleOrDefault(t => t.Id == teamId)?.Name;
                }

                if (message is null || (m.TeamId is not null && teamName is null))
                {
                    return null;
                }

                return new(
                    PushText.ForMessage(message.Sender.Name, teamName, message.Body, message.Kind, message.Attachments.Count > 0, m.Mentioned),
                    new PushData(m.RecipientId, PushKinds.Message, null, PushTarget.Chat(m.TeamId, message.Sender.Id)));

            case ReminderPushJob r:
                var text = r.Kind == PushKinds.LogReminder ? PushText.LogReminder() : PushText.LabourWeekReminder(r.ProjectTitle);
                return new(text, new PushData(r.RecipientId, r.Kind, null, r.Target));

            default:
                return null;
        }
    }

    /// <summary>Unread notifications, counted as the bell counts them, plus unread messages.</summary>
    private async Task<int> BadgeAsync(UserAccess access, CancellationToken cancellationToken)
        => await notifications.CountUnreadAsync(access.UserId, access.VisibleDomains, NotificationAccess.HiddenKinds(access), cancellationToken)
            + await messages.UnreadCountForAsync(access.UserId, cancellationToken);

    /// <summary>A digest update inside the hour only refreshes the badge: no text, no sound.</summary>
    private static PushMessage Build(UserDevice phone, Rendered push, int badge, bool quiet, bool full) => new(
        phone.PushToken,
        full ? push.Text.Title : null,
        full ? push.Text.Body : null,
        push.Data,
        Sound: full && !quiet,
        badge,
        quiet ? PushCategories.QuietChannel : PushCategories.ChannelOf(push.Data.Kind),
        Quiet: quiet || !full);
}
