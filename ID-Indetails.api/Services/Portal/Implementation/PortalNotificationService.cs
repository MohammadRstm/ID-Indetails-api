using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// A client's bell. Only notifications from projects the client still has live access to are shown or counted, so
/// ending access hides them at once; none of them holds what was written.
/// </summary>
public sealed class PortalNotificationService(
    ICurrentPortalAccount currentAccount,
    IPortalDiscussionRepository discussion,
    ILiveUpdates live,
    IClock clock) : IPortalNotificationService
{
    private const int DefaultPage = 20;
    private const int MaxPage = 50;

    public async Task<PortalNotificationPageResponse> ListAsync(long? before, int? limit, CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(limit ?? DefaultPage, 1, MaxPage);
        var rows = await discussion.NotificationsAsync(currentAccount.Id, before, size + 1, cancellationToken);
        var items = rows.Take(size).Select(row =>
        {
            var payload = Read(row.Payload);
            return new PortalNotificationResponse
            {
                Id = row.Id,
                Kind = row.Kind,
                ProjectId = row.ProjectId,
                ProjectTitle = row.ProjectTitle,
                ActorName = payload.AuthorName ?? "Your project team",
                ItemId = row.SubjectId,
                ItemTitle = payload.ItemTitle,
                Count = row.Count,
                CreatedAt = row.CreatedAt,
                Read = row.ReadAt is not null,
            };
        }).ToList();
        return new PortalNotificationPageResponse(items, rows.Count > size);
    }

    public async Task<PortalUnreadResponse> UnreadAsync(CancellationToken cancellationToken = default)
        => new(await discussion.UnreadNotificationsAsync(currentAccount.Id, cancellationToken));

    public async Task<PortalUnreadResponse> MarkReadAsync(PortalNotificationsReadRequest request, CancellationToken cancellationToken = default)
    {
        await discussion.MarkNotificationsReadAsync(currentAccount.Id, request.All ? null : request.Ids, clock.UtcNow, cancellationToken);
        // the client's other tabs and devices
        live.PortalNotificationsChanged([currentAccount.Id]);
        return await UnreadAsync(cancellationToken);
    }

    private sealed record NotificationPayload(string? AuthorName, string? ItemTitle);

    private static NotificationPayload Read(string? json)
        => json is null
            ? new NotificationPayload(null, null)
            : JsonSerializer.Deserialize<NotificationPayload>(json, JsonSerializerOptions.Web) ?? new NotificationPayload(null, null);
}
