using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Notifications;
using ID_Indetails.api.DTO.Request.Notifications;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Notifications;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.api.Services.Notifications.Implementation;

/// <summary>
/// Everyone reads only their own notifications. One about a project of a domain they can no longer see is
/// hidden, and someone else's reads as not found.
/// </summary>
public sealed class NotificationService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    ILiveUpdates live,
    IClock clock) : INotificationService
{
    private const string NotFound = "Notification not found.";

    public async Task<PagedResponse<NotificationResponse>> ListAsync(NotificationListQuery query, CancellationToken cancellationToken = default)
    {
        var (domains, hidden) = await ReaderAsync(cancellationToken);
        var (items, total) = await notifications.SearchAsync(
            currentUser.Id, domains, hidden, query.UnreadOnly, query.Page, query.PageSize, cancellationToken);

        return new PagedResponse<NotificationResponse>(items.Select(ToResponse).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<UnreadCountResponse> UnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var (domains, hidden) = await ReaderAsync(cancellationToken);
        return new(await notifications.CountUnreadAsync(currentUser.Id, domains, hidden, cancellationToken));
    }

    public async Task<UnreadCountResponse> MarkReadAsync(long id, CancellationToken cancellationToken = default)
    {
        var (domains, hidden) = await ReaderAsync(cancellationToken);
        var notification = await notifications.GetForUpdateAsync(id, currentUser.Id, domains, hidden, cancellationToken)
            ?? throw new NotFoundException(NotFound);

        if (notification.ReadAt is null)
        {
            notification.ReadAt = clock.UtcNow;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            live.NotificationsChanged([currentUser.Id]);
        }

        return new UnreadCountResponse(await notifications.CountUnreadAsync(currentUser.Id, domains, hidden, cancellationToken));
    }

    public async Task<UnreadCountResponse> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        await notifications.MarkAllReadAsync(currentUser.Id, clock.UtcNow, cancellationToken);
        live.NotificationsChanged([currentUser.Id]);
        return new UnreadCountResponse(0);
    }

    /// <summary>The domains the reader sees, and the kinds they may no longer read (<see cref="NotificationAccess"/>).</summary>
    private async Task<(IReadOnlyList<string> Domains, IReadOnlyList<string> HiddenKinds)> ReaderAsync(CancellationToken cancellationToken)
    {
        var access = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        return (access.VisibleDomains, NotificationAccess.HiddenKinds(access));
    }

    private static NotificationResponse ToResponse(NotificationView n) => new(
        n.Id,
        n.Kind,
        n.ActorId is { } a ? new NotificationRefResponse(a, n.ActorName ?? string.Empty, n.ActorPhotoFileId) : null,
        n.ProjectId is { } p ? new NotificationProjectResponse(p, n.ProjectTitle ?? string.Empty, n.ProjectDomain ?? string.Empty, n.ProjectIcon, n.ProjectTint) : null,
        n.SubjectType,
        n.SubjectId,
        n.Payload is null ? null : JsonDocument.Parse(n.Payload).RootElement.Clone(),
        n.Count,
        n.CreatedAt,
        n.ReadAt);
}
