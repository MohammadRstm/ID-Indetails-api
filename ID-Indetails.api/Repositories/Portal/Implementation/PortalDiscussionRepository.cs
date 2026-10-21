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

    public async Task<IReadOnlyList<StaffCandidate>> StaffAsync(DiscussionProject project, CancellationToken cancellationToken = default)
    {
        // uploaders of the files the client sees or is to see, not of every project file
        var uploaders = db.PortalItems
            .Where(i => i.ProjectId == project.Id && (i.ForClient || i.PublishedFileId != null))
            .Select(i => i.File.UploadedById);
        var loggers = db.WorkEntries.Where(e => e.ProjectId == project.Id && e.DeletedAt == null).Select(e => e.UserId);
        var people = await db.Users.AsNoTracking()
            .Where(u => u.Status == UserStatus.Active
                && (u.RoleCode == RoleCodes.SuperAdmin || u.Id == project.OwnerId || uploaders.Contains(u.Id) || loggers.Contains(u.Id)))
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.Id)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.RoleCode })
            .ToListAsync(cancellationToken);
        return people
            .Select(u => new StaffCandidate(
                new DiscussionPerson(DiscussionSide.Staff, u.Id, Name(u.FirstName, u.LastName, u.Email)), u.RoleCode == RoleCodes.SuperAdmin))
            .ToList();
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

    public Task<DateTime?> DiscussionVisibleFromAsync(long projectId, CancellationToken cancellationToken = default)
        => db.PortalSettings.AsNoTracking().Where(s => s.ProjectId == projectId).Select(s => s.DiscussionVisibleFrom).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DiscussionMessageRow>> MessagesAsync(
        long projectId, long? before, int take, DateTime? visibleFrom, CancellationToken cancellationToken = default)
    {
        var messages = From(db.PortalMessages.Where(m => m.ProjectId == projectId), visibleFrom);
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

    public Task<long?> NewestMessageIdAsync(long projectId, DateTime? visibleFrom, CancellationToken cancellationToken = default)
        => From(db.PortalMessages.Where(m => m.ProjectId == projectId), visibleFrom).MaxAsync(m => (long?)m.Id, cancellationToken);

    public Task<long?> NewestCommentIdAsync(long projectId, long itemId, DateTime? visibleFrom, CancellationToken cancellationToken = default)
        => From(db.PortalComments.Where(c => c.ProjectId == projectId && c.ItemId == itemId), visibleFrom).MaxAsync(c => (long?)c.Id, cancellationToken);

    public Task<int> UnreadMessagesAsync(
        long projectId, long? after, DiscussionPerson reader, DateTime? visibleFrom, CancellationToken cancellationToken = default)
    {
        var from = after ?? 0;
        var messages = From(db.PortalMessages.Where(m => m.ProjectId == projectId && m.Id > from && m.DeletedAt == null), visibleFrom);
        messages = reader.Side == DiscussionSide.Staff
            ? messages.Where(m => m.AuthorUserId == null || m.AuthorUserId != reader.Id)
            : messages.Where(m => m.AuthorAccountId == null || m.AuthorAccountId != reader.Id);
        return messages.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DiscussionCommentRow>> CommentsAsync(
        long projectId, long? itemId, DateTime? visibleFrom, CancellationToken cancellationToken = default)
    {
        var comments = From(db.PortalComments.Where(c => c.ProjectId == projectId), visibleFrom);
        if (itemId is { } item)
        {
            comments = comments.Where(c => c.ItemId == item);
        }

        return await CommentRows(comments.OrderBy(c => c.Id), cancellationToken);
    }

    public async Task<IReadOnlyList<CommentThreadFacts>> CommentThreadsAsync(
        long projectId, DiscussionPerson reader, DateTime? visibleFrom, CancellationToken cancellationToken = default)
    {
        var comments = From(db.PortalComments.AsNoTracking().Where(c => c.ProjectId == projectId), visibleFrom);
        var threads = await comments
            .GroupBy(c => c.ItemId)
            .Select(g => new { ItemId = g.Key, Count = g.Count(), LastId = g.Max(c => c.Id) })
            .ToListAsync(cancellationToken);
        if (threads.Count == 0)
        {
            return [];
        }

        var lastIds = threads.Select(t => t.LastId).ToList();
        var lasts = await comments
            .Where(c => lastIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ItemTitle, c.FileName, c.CreatedAt })
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        // a comment is unread until the reader's marker on its own file reaches it
        var unread = reader.Side == DiscussionSide.Staff
            ? comments.Where(c => c.DeletedAt == null && (c.AuthorUserId == null || c.AuthorUserId != reader.Id)
                && !db.PortalCommentReads.Any(r => r.ItemId == c.ItemId && r.UserId == reader.Id && r.LastCommentId >= c.Id))
            : comments.Where(c => c.DeletedAt == null && (c.AuthorAccountId == null || c.AuthorAccountId != reader.Id)
                && !db.PortalCommentReads.Any(r => r.ItemId == c.ItemId && r.AccountId == reader.Id && r.LastCommentId >= c.Id));
        var unreadByItem = await unread
            .GroupBy(c => c.ItemId)
            .Select(g => new { ItemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ItemId, g => g.Count, cancellationToken);

        return threads
            .Select(t =>
            {
                var last = lasts[t.LastId];
                return new CommentThreadFacts(
                    t.ItemId, t.Count, t.LastId, last.CreatedAt, last.ItemTitle, last.FileName, unreadByItem.GetValueOrDefault(t.ItemId));
            })
            .ToList();
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

    public Task<PortalCommentRead?> GetCommentReadAsync(long projectId, long itemId, DiscussionPerson reader, CancellationToken cancellationToken = default)
        => reader.Side == DiscussionSide.Staff
            ? db.PortalCommentReads.SingleOrDefaultAsync(r => r.ProjectId == projectId && r.ItemId == itemId && r.UserId == reader.Id, cancellationToken)
            : db.PortalCommentReads.SingleOrDefaultAsync(r => r.ProjectId == projectId && r.ItemId == itemId && r.AccountId == reader.Id, cancellationToken);

    public async Task<IReadOnlyDictionary<long, int>> UnreadForClientAsync(
        long accountId, IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default)
    {
        var unread = new Dictionary<long, int>();
        foreach (var projectId in projectIds)
        {
            var afterMessage = await db.PortalReads.AsNoTracking()
                .Where(r => r.ProjectId == projectId && r.AccountId == accountId)
                .Select(r => r.LastMessageId)
                .SingleOrDefaultAsync(cancellationToken) ?? 0;
            var visibleFrom = await DiscussionVisibleFromAsync(projectId, cancellationToken);
            var messages = await From(db.PortalMessages, visibleFrom).CountAsync(
                m => m.ProjectId == projectId && m.Id > afterMessage && m.DeletedAt == null
                    && (m.AuthorAccountId == null || m.AuthorAccountId != accountId),
                cancellationToken);
            // a comment is unread until the client's marker on its own file reaches it
            var comments = await From(db.PortalComments, visibleFrom).CountAsync(
                c => c.ProjectId == projectId && c.DeletedAt == null && (c.AuthorAccountId == null || c.AuthorAccountId != accountId)
                    && !db.PortalCommentReads.Any(r => r.ItemId == c.ItemId && r.AccountId == accountId && r.LastCommentId >= c.Id),
                cancellationToken);
            unread[projectId] = messages + comments;
        }

        return unread;
    }

    public Task AddNotificationAsync(PortalNotification notification, bool rolling, CancellationToken cancellationToken = default)
    {
        var kind = SnakeCaseEnum.ToDb(notification.Kind);

        // MySQL makes the updates left to right, so the count reads read_at before it is cleared
        return rolling
            ? db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO `portal_notifications` (`account_id`, `project_id`, `kind`, `subject_id`, `payload`, `digest_key`, `count`, `created_at`)
                VALUES ({notification.AccountId}, {notification.ProjectId}, {kind}, {notification.SubjectId}, {notification.Payload},
                    {notification.DigestKey}, {notification.Count}, {notification.CreatedAt}) AS new
                ON DUPLICATE KEY UPDATE
                    `count` = IF(`portal_notifications`.`read_at` IS NULL, `portal_notifications`.`count` + 1, 1),
                    `read_at` = NULL,
                    `created_at` = new.`created_at`,
                    `payload` = new.`payload`,
                    `subject_id` = new.`subject_id`
                """,
                cancellationToken)
            : db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO `portal_notifications` (`account_id`, `project_id`, `kind`, `subject_id`, `payload`, `digest_key`, `count`, `created_at`)
                VALUES ({notification.AccountId}, {notification.ProjectId}, {kind}, {notification.SubjectId}, {notification.Payload},
                    {notification.DigestKey}, {notification.Count}, {notification.CreatedAt}) AS new
                ON DUPLICATE KEY UPDATE `count` = `portal_notifications`.`count`
                """,
                cancellationToken);
    }

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

    public void Add(PortalCommentRead read) => db.PortalCommentReads.Add(read);

    public void Remove(PortalMention mention) => db.PortalMentions.Remove(mention);

    /// <summary>
    /// A client's notifications on projects they still have live access to (ended access hides them at once), and none
    /// from before the project moved to another client.
    /// </summary>
    private IQueryable<PortalNotification> Visible(long accountId)
        => db.PortalNotifications.AsNoTracking()
            .Where(n => n.AccountId == accountId
                && db.PortalProjectAccess.Any(a => a.AccountId == accountId && a.ProjectId == n.ProjectId && a.RevokedAt == null)
                && !db.PortalSettings.Any(s => s.ProjectId == n.ProjectId && s.DiscussionVisibleFrom != null && n.CreatedAt < s.DiscussionVisibleFrom));

    /// <summary>Only what was written from <paramref name="visibleFrom"/> on, when there is a cut-off.</summary>
    private static IQueryable<PortalMessage> From(IQueryable<PortalMessage> messages, DateTime? visibleFrom)
        => visibleFrom is { } from ? messages.Where(m => m.CreatedAt >= from) : messages;

    private static IQueryable<PortalComment> From(IQueryable<PortalComment> comments, DateTime? visibleFrom)
        => visibleFrom is { } from ? comments.Where(c => c.CreatedAt >= from) : comments;

    private async Task<IReadOnlyList<DiscussionMessageRow>> MessageRows(IQueryable<PortalMessage> messages, CancellationToken cancellationToken)
    {
        var rows = await (from m in messages.AsNoTracking()
                          join u in db.Users on m.AuthorUserId equals u.Id into users
                          from u in users.DefaultIfEmpty()
                          join a in db.PortalAccounts on m.AuthorAccountId equals a.Id into accounts
                          from a in accounts.DefaultIfEmpty()
                          select new
                          {
                              m.Id, m.AuthorUserId, m.AuthorAccountId, m.Body, m.CreatedAt, m.EditedAt, m.DeletedAt,
                              UserFirst = u == null ? null : u.FirstName, UserLast = u == null ? null : u.LastName, UserEmail = u == null ? null : u.Email,
                              ClientFirst = a == null ? null : a.FirstName, ClientLast = a == null ? null : a.LastName, ClientEmail = a == null ? null : a.Email,
                          })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new DiscussionMessageRow(
                r.Id, r.AuthorUserId, r.AuthorAccountId,
                r.AuthorUserId is not null ? Name(r.UserFirst, r.UserLast, r.UserEmail) : Name(r.ClientFirst, r.ClientLast, r.ClientEmail),
                r.Body, r.CreatedAt, r.EditedAt, r.DeletedAt))
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
