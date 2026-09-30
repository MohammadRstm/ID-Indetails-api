using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Files.Abstraction;
using ID_Indetails.api.Repositories.Messaging.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Messaging.Implementation;

public sealed class MessagingRepository(IdIndetailsDbContext db) : IMessagingRepository
{
    public async Task<IReadOnlyList<ChatPerson>> PeopleAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.ToList();
        return await People(db.Users.Where(u => list.Contains(u.Id)), cancellationToken);
    }

    public Task<IReadOnlyList<ChatPerson>> ActivePeopleAsync(CancellationToken cancellationToken = default)
        => People(db.Users.Where(u => u.Status == UserStatus.Active), cancellationToken);

    public async Task<IReadOnlyList<ChatTeam>> TeamsAsync(CancellationToken cancellationToken = default)
        => await db.Teams.AsNoTracking()
            .OrderBy(t => t.DomainCode).ThenBy(t => t.Name)
            .Select(t => new ChatTeam(t.Id, t.Name, t.DomainCode))
            .ToListAsync(cancellationToken);

    public Task<Conversation?> ConversationAsync(long id, CancellationToken cancellationToken = default)
        => db.Conversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Conversation?> TeamConversationAsync(long teamId, CancellationToken cancellationToken = default)
        => db.Conversations.SingleOrDefaultAsync(c => c.TeamId == teamId, cancellationToken);

    public Task<Conversation?> DirectConversationAsync(long userA, long userB, CancellationToken cancellationToken = default)
    {
        var (low, high) = userA < userB ? (userA, userB) : (userB, userA);
        return db.Conversations.SingleOrDefaultAsync(c => c.DmUserLowId == low && c.DmUserHighId == high, cancellationToken);
    }

    public async Task<Conversation?> TeamConversationForUpdateAsync(long teamId, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlAsync($"SELECT `id` FROM `teams` WHERE `id` = {teamId} FOR UPDATE", cancellationToken);
        return (await db.Conversations
                .FromSql($"SELECT * FROM `conversations` WHERE `team_id` = {teamId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
    }

    public async Task<Conversation?> DirectConversationForUpdateAsync(long userA, long userB, CancellationToken cancellationToken = default)
    {
        var (low, high) = userA < userB ? (userA, userB) : (userB, userA);
        await db.Database.ExecuteSqlAsync($"SELECT `id` FROM `users` WHERE `id` = {low} FOR UPDATE", cancellationToken);
        return (await db.Conversations
                .FromSql($"SELECT * FROM `conversations` WHERE `dm_user_low_id` = {low} AND `dm_user_high_id` = {high} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
    }

    public void Add(Conversation conversation) => db.Conversations.Add(conversation);

    public void Add(Message message) => db.Messages.Add(message);

    public Task<IReadOnlyList<MessageView>> MessagesAsync(long conversationId, long? beforeId, int limit, CancellationToken cancellationToken = default)
    {
        var query = db.Messages.Where(m => m.ConversationId == conversationId);
        if (beforeId is { } before)
        {
            query = query.Where(m => m.Id < before);
        }

        return Views(query.OrderByDescending(m => m.Id).Take(limit), cancellationToken);
    }

    public Task<IReadOnlyList<MessageView>> MessagesByIdAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.ToList();
        return Views(db.Messages.Where(m => list.Contains(m.Id)), cancellationToken);
    }

    public Task<long?> LastMessageIdAsync(long conversationId, CancellationToken cancellationToken = default)
        => db.Messages.Where(m => m.ConversationId == conversationId).MaxAsync(m => (long?)m.Id, cancellationToken);

    public Task<ConversationRead?> ReadForUpdateAsync(long userId, long conversationId, CancellationToken cancellationToken = default)
        => db.ConversationReads.SingleOrDefaultAsync(r => r.UserId == userId && r.ConversationId == conversationId, cancellationToken);

    public void Add(ConversationRead read) => db.ConversationReads.Add(read);

    public async Task<IReadOnlyList<ConversationState>> StatesAsync(
        long userId,
        IReadOnlyCollection<long> teamIds,
        CancellationToken cancellationToken = default)
    {
        var teams = teamIds.ToList();
        var conversations = await db.Conversations.AsNoTracking()
            .Where(c => (c.TeamId != null && teams.Contains(c.TeamId.Value)) || c.DmUserLowId == userId || c.DmUserHighId == userId)
            .Select(c => new
            {
                c.Id,
                c.Type,
                c.TeamId,
                OtherUserId = c.DmUserLowId == userId ? c.DmUserHighId : c.DmUserLowId,
                LastMessageId = c.Messages.Max(m => (long?)m.Id),
            })
            .ToListAsync(cancellationToken);
        if (conversations.Count == 0)
        {
            return [];
        }

        var ids = conversations.Select(c => c.Id).ToList();
        var unread = await (
                from m in Unread(userId, ids)
                group m by m.ConversationId into g
                select new { ConversationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConversationId, x => x.Count, cancellationToken);
        var mentioned = (await (
                from m in Unread(userId, ids)
                join x in db.MessageMentions on m.Id equals x.MessageId
                where x.UserId == userId
                select m.ConversationId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        return conversations
            .Select(c => new ConversationState(
                c.Id,
                c.Type,
                c.TeamId,
                c.Type == ConversationType.Dm ? c.OtherUserId : null,
                c.LastMessageId,
                unread.GetValueOrDefault(c.Id),
                mentioned.Contains(c.Id)))
            .ToList();
    }

    /// <summary>Messages others sent in these conversations after the user's read marker.</summary>
    private IQueryable<Message> Unread(long userId, List<long> conversationIds)
        => from m in db.Messages.AsNoTracking()
           join r in db.ConversationReads.Where(r => r.UserId == userId) on m.ConversationId equals r.ConversationId into reads
           from r in reads.DefaultIfEmpty()
           where conversationIds.Contains(m.ConversationId)
               && m.SenderId != userId
               && (r == null || r.LastReadMessageId == null || m.Id > r.LastReadMessageId)
           select m;

    private static async Task<IReadOnlyList<ChatPerson>> People(IQueryable<User> users, CancellationToken cancellationToken)
        => (await users.AsNoTracking()
                .Select(u => new
                {
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.RoleCode,
                    u.DomainCode,
                    u.TeamId,
                    TeamName = u.Team != null ? u.Team.Name : null,
                    u.Position,
                    u.Status,
                    u.PhotoFileId,
                })
                .ToListAsync(cancellationToken))
            .Select(u => new ChatPerson(
                u.Id, PersonName.Of(u.FirstName, u.LastName, u.Email), u.RoleCode, u.DomainCode, u.TeamId, u.TeamName, u.Position, u.Status, u.PhotoFileId))
            .OrderBy(p => p.Name)
            .ToList();

    private static async Task<IReadOnlyList<MessageView>> Views(IQueryable<Message> messages, CancellationToken cancellationToken)
        => (await messages.AsNoTracking()
                .Select(m => new
                {
                    m.Id,
                    m.ConversationId,
                    m.SenderId,
                    m.Sender.FirstName,
                    m.Sender.LastName,
                    m.Sender.Email,
                    m.Body,
                    m.Kind,
                    m.RefEntryId,
                    m.RefCommentId,
                    m.CreatedAt,
                    Mentions = m.Mentions.Select(x => new { x.UserId, x.User.FirstName, x.User.LastName, x.User.Email }).ToList(),
                    Attachments = m.Attachments
                        .OrderBy(a => a.FileId)
                        .Select(a => new FileView(a.FileId, a.File.OriginalName, a.File.MimeType, a.File.SizeBytes, a.File.CreatedAt))
                        .ToList(),
                })
                .ToListAsync(cancellationToken))
            .Select(m => new MessageView(
                m.Id,
                m.ConversationId,
                new MessagePerson(m.SenderId, PersonName.Of(m.FirstName, m.LastName, m.Email)),
                m.Body,
                m.Kind,
                m.RefEntryId,
                m.RefCommentId,
                m.Mentions.Select(x => new MessagePerson(x.UserId, PersonName.Of(x.FirstName, x.LastName, x.Email))).OrderBy(x => x.Name).ToList(),
                m.Attachments,
                m.CreatedAt))
            .ToList();
}
