using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Notifications.Implementation;

public sealed class NotificationRepository(IdIndetailsDbContext db) : INotificationRepository
{
    public void Add(Notification notification) => db.Notifications.Add(notification);

    public async Task<Notification?> GetDigestForUpdateAsync(long recipientId, string digestKey, CancellationToken cancellationToken = default)
        => (await db.Notifications
                .FromSql($"SELECT * FROM `notifications` WHERE `recipient_id` = {recipientId} AND `digest_key` = {digestKey} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();

    public async Task<IReadOnlySet<long>> RecipientsWithKeyAsync(
        string digestKey,
        IReadOnlyCollection<long> recipientIds,
        CancellationToken cancellationToken = default)
        => (await db.Notifications.AsNoTracking()
                .Where(n => recipientIds.Contains(n.RecipientId) && n.DigestKey == digestKey)
                .Select(n => n.RecipientId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

    public async Task<IReadOnlyList<long>> PeopleWhoMayHoldAsync(
        string domainCode,
        string permissionCode,
        CancellationToken cancellationToken = default)
        => await db.Users
            .AsNoTracking()
            .Where(u => u.Status == UserStatus.Active
                && (u.DomainCode == domainCode
                    || u.RoleCode == RoleCodes.SuperAdmin
                    || db.UserPermissionOverrides.Any(o => o.UserId == u.Id && o.PermissionCode == permissionCode && o.Effect == PermissionEffect.Grant)))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<NotificationView> Items, int TotalCount)> SearchAsync(
        long recipientId,
        IReadOnlyList<string> visibleDomains,
        IReadOnlyList<string> hiddenKinds,
        bool unreadOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = Visible(recipientId, visibleDomains, hiddenKinds).AsNoTracking();
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (await DescribeAsync(rows, cancellationToken), total);
    }

    public async Task<NotificationView?> GetVisibleAsync(
        long id,
        long recipientId,
        IReadOnlyList<string> visibleDomains,
        IReadOnlyList<string> hiddenKinds,
        CancellationToken cancellationToken = default)
    {
        var row = await Visible(recipientId, visibleDomains, hiddenKinds).AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
        return row is null ? null : (await DescribeAsync([row], cancellationToken))[0];
    }

    /// <summary>The rows with their actor's name and photo and their project's title and tile, in the same order.</summary>
    private async Task<IReadOnlyList<NotificationView>> DescribeAsync(IReadOnlyList<Notification> rows, CancellationToken cancellationToken)
    {
        var actorIds = rows.Where(n => n.ActorId != null).Select(n => n.ActorId!.Value).Distinct().ToList();
        var projectIds = rows.Where(n => n.ProjectId != null).Select(n => n.ProjectId!.Value).Distinct().ToList();
        var actors = (await db.Users.AsNoTracking()
                .Where(u => actorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.PhotoFileId })
                .ToListAsync(cancellationToken))
            .ToDictionary(u => u.Id, u => (Name: PersonName.Of(u.FirstName, u.LastName, u.Email), u.PhotoFileId));
        var projects = await db.Projects.AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, p.DomainCode, p.Icon, p.Tint })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return rows.Select(n =>
        {
            var project = n.ProjectId is { } p ? projects.GetValueOrDefault(p) : null;
            (string Name, long? PhotoFileId)? actor = n.ActorId is { } a && actors.TryGetValue(a, out var found) ? found : null;
            return new NotificationView(
                n.Id,
                n.Kind,
                n.ActorId,
                actor?.Name,
                actor?.PhotoFileId,
                n.ProjectId,
                project?.Title,
                project?.DomainCode,
                project?.Icon,
                project?.Tint,
                n.SubjectType,
                n.SubjectId,
                n.Payload,
                n.Count,
                n.CreatedAt,
                n.ReadAt);
        }).ToList();
    }

    public Task<int> CountUnreadAsync(long recipientId, IReadOnlyList<string> visibleDomains, IReadOnlyList<string> hiddenKinds, CancellationToken cancellationToken = default)
        => Visible(recipientId, visibleDomains, hiddenKinds).AsNoTracking().CountAsync(n => n.ReadAt == null, cancellationToken);

    public Task<Notification?> GetForUpdateAsync(long id, long recipientId, IReadOnlyList<string> visibleDomains, IReadOnlyList<string> hiddenKinds, CancellationToken cancellationToken = default)
        => Visible(recipientId, visibleDomains, hiddenKinds).SingleOrDefaultAsync(n => n.Id == id, cancellationToken);

    public Task MarkAllReadAsync(long recipientId, DateTime readAt, CancellationToken cancellationToken = default)
        => db.Notifications
            .Where(n => n.RecipientId == recipientId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, readAt), cancellationToken);

    /// <summary>The recipient's notifications, less those about a project they can no longer see, and of a kind they may no longer read.</summary>
    private IQueryable<Notification> Visible(long recipientId, IReadOnlyList<string> visibleDomains, IReadOnlyList<string> hiddenKinds)
    {
        var domains = visibleDomains.ToList();
        var hidden = hiddenKinds.ToList();
        return db.Notifications.Where(n => n.RecipientId == recipientId
            && !hidden.Contains(n.Kind)
            && (n.ProjectId == null || db.Projects.Any(p => p.Id == n.ProjectId && domains.Contains(p.DomainCode))));
    }
}
