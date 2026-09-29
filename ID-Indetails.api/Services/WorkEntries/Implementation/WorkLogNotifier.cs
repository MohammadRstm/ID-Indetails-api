using System.Text.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

/// <summary>
/// Recipients are the active people who see everyone's work in the domain (<c>work_entry:view_all</c>),
/// never the author. <c>all</c> sends one notification per entry, <c>long</c> only for entries of 3 hours
/// or more, <c>digest</c> keeps one notification per recipient per day that sums the day's work, and
/// <c>off</c> sends nothing.
/// </summary>
public sealed class WorkLogNotifier(
    INotificationSender sender,
    IWorkEntryRepository entries,
    INotificationRepository notifications,
    ILiveUpdates live,
    IClock clock) : IWorkLogNotifier
{
    public const int LongEntryMinutes = 180;

    private sealed record DigestPayload(int Minutes, string LatestName, string ProjectTitle);

    public async Task NotifyAsync(
        WorkEntry entry,
        string projectTitle,
        string authorName,
        EntryNotificationMode mode,
        CancellationToken cancellationToken = default)
    {
        var minutes = (int)(entry.EndedAt - entry.StartedAt).TotalMinutes;
        if (mode == EntryNotificationMode.Off || (mode == EntryNotificationMode.Long && minutes < LongEntryMinutes))
        {
            return;
        }

        var recipients = await RecipientsAsync(entry.UserId, cancellationToken);
        if (recipients.Count == 0)
        {
            return;
        }

        if (mode != EntryNotificationMode.Digest)
        {
            await sender.SendAsync(
                new NotificationDraft(
                    NotificationKinds.WorkLogged,
                    entry.UserId,
                    DomainCodes.Design,
                    entry.ProjectId,
                    "work_entry",
                    entry.Id,
                    new { authorName, projectTitle, minutes, description = entry.Description }),
                recipients,
                cancellationToken);
            return;
        }

        await entries.LockDomainAsync(DomainCodes.Design, cancellationToken);
        live.NotificationsChanged(recipients);
        var digestKey = $"work-digest-{clock.CompanyToday:yyyy-MM-dd}";
        foreach (var recipientId in recipients)
        {
            var digest = await notifications.GetDigestForUpdateAsync(recipientId, digestKey, cancellationToken);
            if (digest is null)
            {
                notifications.Add(new Notification
                {
                    RecipientId = recipientId,
                    Kind = NotificationKinds.WorkDigest,
                    ActorId = entry.UserId,
                    ProjectId = entry.ProjectId,
                    DigestKey = digestKey,
                    Count = 1,
                    Payload = JsonSerializer.Serialize(new DigestPayload(minutes, authorName, projectTitle), JsonSerializerOptions.Web),
                    CreatedAt = clock.UtcNow,
                });
                continue;
            }

            var sofar = digest.Payload is null ? null : JsonSerializer.Deserialize<DigestPayload>(digest.Payload, JsonSerializerOptions.Web);
            digest.Count++;
            digest.ActorId = entry.UserId;
            digest.ProjectId = entry.ProjectId;
            digest.Payload = JsonSerializer.Serialize(new DigestPayload((sofar?.Minutes ?? 0) + minutes, authorName, projectTitle), JsonSerializerOptions.Web);
            digest.CreatedAt = clock.UtcNow;
            digest.ReadAt = null;
        }
    }

    private async Task<IReadOnlyList<long>> RecipientsAsync(long authorId, CancellationToken cancellationToken)
        => (await sender.HoldersAsync(DomainCodes.Design, PermissionCodes.WorkEntryViewAll, cancellationToken))
            .Where(id => id != authorId)
            .ToList();
}
