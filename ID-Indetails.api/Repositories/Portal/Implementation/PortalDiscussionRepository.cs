using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Portal.Implementation;

public sealed class PortalDiscussionRepository(IdIndetailsDbContext db) : IPortalDiscussionRepository
{
    public Task<DiscussionProject?> ProjectAsync(long projectId, CancellationToken cancellationToken = default)
        => db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new DiscussionProject(p.Id, p.DomainCode, p.Title, p.Status, p.BillingType, p.ClientId, p.OwnerId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DiscussionPerson>> StaffAsync(DiscussionProject project, CancellationToken cancellationToken = default)
    {
        var uploaders = db.PortalFiles.Where(f => f.ProjectId == project.Id).Select(f => f.UploadedById);
        var loggers = db.WorkEntries.Where(e => e.ProjectId == project.Id && e.DeletedAt == null).Select(e => e.UserId);
        var people = await db.Users.AsNoTracking()
            .Where(u => u.Status == UserStatus.Active
                && (u.RoleCode == RoleCodes.SuperAdmin || u.Id == project.OwnerId || uploaders.Contains(u.Id) || loggers.Contains(u.Id)))
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.Id)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToListAsync(cancellationToken);
        return people.Select(u => new DiscussionPerson(DiscussionSide.Staff, u.Id, Name(u.FirstName, u.LastName, u.Email))).ToList();
    }

    public async Task<IReadOnlyList<DiscussionPerson>> ClientsAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var people = await (from access in db.PortalProjectAccess.AsNoTracking()
                            where access.ProjectId == projectId && access.RevokedAt == null
                            join account in db.PortalAccounts on access.AccountId equals account.Id
                            where account.Status == PortalAccountStatus.Active
                            orderby account.FirstName, account.LastName, account.Id
                            select new { account.Id, account.FirstName, account.LastName, account.Email })
            .ToListAsync(cancellationToken);
        return people.Select(a => new DiscussionPerson(DiscussionSide.Client, a.Id, Name(a.FirstName, a.LastName, a.Email))).ToList();
    }

    public async Task<IReadOnlyList<DiscussionMessageRow>> MessagesAsync(
        long projectId, long? before, int take, CancellationToken cancellationToken = default)
    {
        var messages = db.PortalMessages.Where(m => m.ProjectId == projectId);
        if (before is { } cursor)
        {
            messages = messages.Where(m => m.Id < cursor);
        }

        return await MessageRows(messages.OrderByDescending(m => m.Id).Take(take), cancellationToken);
    }

    public async Task<DiscussionMessageRow?> MessageRowAsync(long projectId, long messageId, CancellationToken cancellationToken = default)
        => (await MessageRows(db.PortalMessages.Where(m => m.ProjectId == projectId && m.Id == messageId), cancellationToken)).SingleOrDefault();

    public Task<PortalMessage?> GetMessageAsync(long projectId, long messageId, CancellationToken cancellationToken = default)
        => db.PortalMessages.SingleOrDefaultAsync(m => m.ProjectId == projectId && m.Id == messageId, cancellationToken);

    public Task<PortalMessage?> FindMessageAsync(DiscussionPerson author, string clientToken, CancellationToken cancellationToken = default)
        => author.Side == DiscussionSide.Staff
            ? db.PortalMessages.SingleOrDefaultAsync(m => m.AuthorUserId == author.Id && m.ClientToken == clientToken, cancellationToken)
            : db.PortalMessages.SingleOrDefaultAsync(m => m.AuthorAccountId == author.Id && m.ClientToken == clientToken, cancellationToken);

    public Task<long?> NewestMessageIdAsync(long projectId, CancellationToken cancellationToken = default)
        => db.PortalMessages.Where(m => m.ProjectId == projectId).MaxAsync(m => (long?)m.Id, cancellationToken);

    public Task<long?> NewestCommentIdAsync(long projectId, CancellationToken cancellationToken = default)
        => db.PortalComments.Where(c => c.ProjectId == projectId).MaxAsync(c => (long?)c.Id, cancellationToken);

    public Task<int> UnreadMessagesAsync(long projectId, long? after, DiscussionPerson reader, CancellationToken cancellationToken = default)
    {
        var from = after ?? 0;
        var messages = db.PortalMessages.Where(m => m.ProjectId == projectId && m.Id > from);
        messages = reader.Side == DiscussionSide.Staff
            ? messages.Where(m => m.AuthorUserId == null || m.AuthorUserId != reader.Id)
            : messages.Where(m => m.AuthorAccountId == null || m.AuthorAccountId != reader.Id);
        return messages.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DiscussionCommentRow>> CommentsAsync(long projectId, long? itemId, CancellationToken cancellationToken = default)
    {
        var comments = db.PortalComments.Where(c => c.ProjectId == projectId);
        if (itemId is { } item)
        {
            comments = comments.Where(c => c.ItemId == item);
        }

        return await CommentRows(comments.OrderBy(c => c.Id), cancellationToken);
    }

    public async Task<DiscussionCommentRow?> CommentRowAsync(long projectId, long commentId, CancellationToken cancellationToken = default)
        => (await CommentRows(db.PortalComments.Where(c => c.ProjectId == projectId && c.Id == commentId), cancellationToken)).SingleOrDefault();

    public Task<PortalComment?> GetCommentAsync(long projectId, long commentId, CancellationToken cancellationToken = default)
        => db.PortalComments.SingleOrDefaultAsync(c => c.ProjectId == projectId && c.Id == commentId, cancellationToken);

    public Task<PortalComment?> FindCommentAsync(DiscussionPerson author, string clientToken, CancellationToken cancellationToken = default)
        => author.Side == DiscussionSide.Staff
            ? db.PortalComments.SingleOrDefaultAsync(c => c.AuthorUserId == author.Id && c.ClientToken == clientToken, cancellationToken)
            : db.PortalComments.SingleOrDefaultAsync(c => c.AuthorAccountId == author.Id && c.ClientToken == clientToken, cancellationToken);

    public async Task<IReadOnlyDictionary<long, PublishedItemFacts>> PublishedItemsAsync(long projectId, CancellationToken cancellationToken = default)
        => await (from item in db.PortalItems.AsNoTracking()
                  where item.ProjectId == projectId && item.PublishedFileId != null
                  join file in db.PortalFiles on item.PublishedFileId equals file.Id
                  select new PublishedItemFacts(item.Id, item.PublishedTitle!, file.Id, file.OriginalName, file.UploadedById))
            .ToDictionaryAsync(i => i.ItemId, cancellationToken);

    public async Task<ILookup<long, string>> MessageMentionsAsync(IReadOnlyCollection<long> messageIds, CancellationToken cancellationToken = default)
    {
        var rows = await db.PortalMentions.AsNoTracking()
            .Where(m => m.MessageId != null && messageIds.Contains(m.MessageId.Value))
            .Select(m => new { m.MessageId, m.UserId, m.AccountId })
            .ToListAsync(cancellationToken);
        return rows.ToLookup(r => r.MessageId!.Value, r => KeyOf(r.UserId, r.AccountId));
    }

    public async Task<ILookup<long, string>> CommentMentionsAsync(IReadOnlyCollection<long> commentIds, CancellationToken cancellationToken = default)
    {
        var rows = await db.PortalMentions.AsNoTracking()
            .Where(m => m.CommentId != null && commentIds.Contains(m.CommentId.Value))
            .Select(m => new { m.CommentId, m.UserId, m.AccountId })
            .ToListAsync(cancellationToken);
        return rows.ToLookup(r => r.CommentId!.Value, r => KeyOf(r.UserId, r.AccountId));
    }

    public async Task<IReadOnlyList<PortalMention>> MentionsAsync(long? messageId, long? commentId, CancellationToken cancellationToken = default)
        => await db.PortalMentions.Where(m => (messageId != null && m.MessageId == messageId) || (commentId != null && m.CommentId == commentId))
            .ToListAsync(cancellationToken);

    public Task<PortalRead?> GetReadAsync(long projectId, DiscussionPerson reader, CancellationToken cancellationToken = default)
        => reader.Side == DiscussionSide.Staff
            ? db.PortalReads.SingleOrDefaultAsync(r => r.ProjectId == projectId && r.UserId == reader.Id, cancellationToken)
            : db.PortalReads.SingleOrDefaultAsync(r => r.ProjectId == projectId && r.AccountId == reader.Id, cancellationToken);

    public async Task<IReadOnlyDictionary<long, int>> UnreadForClientAsync(
        long accountId, IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default)
    {
        var unread = new Dictionary<long, int>();
        foreach (var projectId in projectIds)
        {
            var marker = await db.PortalReads.AsNoTracking()
                .Where(r => r.ProjectId == projectId && r.AccountId == accountId)
                .Select(r => new { r.LastMessageId, r.LastCommentId })
                .SingleOrDefaultAsync(cancellationToken);
            var afterMessage = marker?.LastMessageId ?? 0;
            var afterComment = marker?.LastCommentId ?? 0;
            var messages = await db.PortalMessages.CountAsync(
                m => m.ProjectId == projectId && m.Id > afterMessage && (m.AuthorAccountId == null || m.AuthorAccountId != accountId), cancellationToken);
            var comments = await db.PortalComments.CountAsync(
                c => c.ProjectId == projectId && c.Id > afterComment && c.DeletedAt == null && (c.AuthorAccountId == null || c.AuthorAccountId != accountId),
                cancellationToken);
            unread[projectId] = messages + comments;
        }

        return unread;
    }

    public async Task<PortalNotification?> GetNotificationForUpdateAsync(long accountId, string digestKey, CancellationToken cancellationToken = default)
        => (await db.PortalNotifications
            .FromSql($"SELECT * FROM `portal_notifications` WHERE `account_id` = {accountId} AND `digest_key` = {digestKey} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<PortalNotificationRow>> NotificationsAsync(
        long accountId, long? before, int take, CancellationToken cancellationToken = default)
    {
        var rows = Visible(accountId);
        if (before is { } cursor)
        {
            rows = rows.Where(n => n.Id < cursor);
        }

        return await (from n in rows.OrderByDescending(n => n.Id).Take(take)
                      join p in db.Projects on n.ProjectId equals p.Id
                      orderby n.Id descending
                      select new PortalNotificationRow(n.Id, n.Kind, n.ProjectId, p.Title, n.SubjectId, n.Payload, n.Count, n.CreatedAt, n.ReadAt))
            .ToListAsync(cancellationToken);
    }

    public Task<int> UnreadNotificationsAsync(long accountId, CancellationToken cancellationToken = default)
        => Visible(accountId).CountAsync(n => n.ReadAt == null, cancellationToken);

    public Task MarkNotificationsReadAsync(long accountId, IReadOnlyCollection<long>? ids, DateTime now, CancellationToken cancellationToken = default)
    {
        var unread = db.PortalNotifications.Where(n => n.AccountId == accountId && n.ReadAt == null);
        if (ids is not null)
        {
            unread = unread.Where(n => ids.Contains(n.Id));
        }

        return unread.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);
    }

    public void Add(PortalMessage message) => db.PortalMessages.Add(message);

    public void Add(PortalComment comment) => db.PortalComments.Add(comment);

    public void Add(PortalMention mention) => db.PortalMentions.Add(mention);

    public void Add(PortalRead read) => db.PortalReads.Add(read);

    public void Add(PortalNotification notification) => db.PortalNotifications.Add(notification);

    public void Remove(PortalMention mention) => db.PortalMentions.Remove(mention);

    /// <summary>A client's notifications on projects they still have live access to: ended access hides them at once.</summary>
    private IQueryable<PortalNotification> Visible(long accountId)
        => db.PortalNotifications.AsNoTracking()
            .Where(n => n.AccountId == accountId
                && db.PortalProjectAccess.Any(a => a.AccountId == accountId && a.ProjectId == n.ProjectId && a.RevokedAt == null));

    private async Task<IReadOnlyList<DiscussionMessageRow>> MessageRows(IQueryable<PortalMessage> messages, CancellationToken cancellationToken)
    {
        var rows = await (from m in messages.AsNoTracking()
                          join u in db.Users on m.AuthorUserId equals u.Id into users
                          from u in users.DefaultIfEmpty()
                          join a in db.PortalAccounts on m.AuthorAccountId equals a.Id into accounts
                          from a in accounts.DefaultIfEmpty()
                          select new
                          {
                              m.Id, m.AuthorUserId, m.AuthorAccountId, m.Body, m.CreatedAt, m.EditedAt,
                              UserFirst = u == null ? null : u.FirstName, UserLast = u == null ? null : u.LastName, UserEmail = u == null ? null : u.Email,
                              ClientFirst = a == null ? null : a.FirstName, ClientLast = a == null ? null : a.LastName, ClientEmail = a == null ? null : a.Email,
                          })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new DiscussionMessageRow(
                r.Id, r.AuthorUserId, r.AuthorAccountId,
                r.AuthorUserId is not null ? Name(r.UserFirst, r.UserLast, r.UserEmail) : Name(r.ClientFirst, r.ClientLast, r.ClientEmail),
                r.Body, r.CreatedAt, r.EditedAt))
            .ToList();
    }

    private async Task<IReadOnlyList<DiscussionCommentRow>> CommentRows(IQueryable<PortalComment> comments, CancellationToken cancellationToken)
    {
        var rows = await (from c in comments.AsNoTracking()
                          join u in db.Users on c.AuthorUserId equals u.Id into users
                          from u in users.DefaultIfEmpty()
                          join a in db.PortalAccounts on c.AuthorAccountId equals a.Id into accounts
                          from a in accounts.DefaultIfEmpty()
                          select new
                          {
                              c.Id, c.ItemId, c.FileId, c.ItemTitle, c.FileName, c.AuthorUserId, c.AuthorAccountId, c.Body, c.CreatedAt, c.EditedAt, c.DeletedAt,
                              UserFirst = u == null ? null : u.FirstName, UserLast = u == null ? null : u.LastName, UserEmail = u == null ? null : u.Email,
                              ClientFirst = a == null ? null : a.FirstName, ClientLast = a == null ? null : a.LastName, ClientEmail = a == null ? null : a.Email,
                          })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new DiscussionCommentRow(
                r.Id, r.ItemId, r.FileId, r.ItemTitle, r.FileName, r.AuthorUserId, r.AuthorAccountId,
                r.AuthorUserId is not null ? Name(r.UserFirst, r.UserLast, r.UserEmail) : Name(r.ClientFirst, r.ClientLast, r.ClientEmail),
                r.Body, r.CreatedAt, r.EditedAt, r.DeletedAt))
            .ToList();
    }

    private static string Name(string? first, string? last, string? email)
    {
        var name = $"{first} {last}".Trim();
        return name.Length > 0 ? name : email ?? "Someone";
    }

    private static string KeyOf(long? userId, long? accountId)
        => userId is { } staff ? PortalDiscussionRules.KeyOf(DiscussionSide.Staff, staff) : PortalDiscussionRules.KeyOf(DiscussionSide.Client, accountId!.Value);
}
