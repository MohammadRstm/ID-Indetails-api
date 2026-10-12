using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Proton.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Proton.Implementation;

public sealed class ProtonHistoryRepository(IdIndetailsDbContext db) : IProtonHistoryRepository
{
    /// <summary>What a deleted chat's question becomes in the usage ledger (the column can't be empty).</summary>
    public const string DeletedText = "[deleted]";

    public void Add(ProtonStoredConversation conversation) => db.ProtonConversations.Add(conversation);

    public void Add(ProtonStoredMessage message) => db.ProtonConversationMessages.Add(message);

    public Task<ProtonStoredConversation?> FindAsync(long userId, Guid id, CancellationToken cancellationToken = default)
        => db.ProtonConversations.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);

    public async Task<(IReadOnlyList<ProtonStoredConversation> Items, int Total)> ListAsync(
        long userId, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = db.ProtonConversations.AsNoTracking().Where(c => c.UserId == userId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.Title.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<ProtonStoredMessage>> MessagesAsync(Guid id, CancellationToken cancellationToken = default)
        => await db.ProtonConversationMessages.AsNoTracking().Where(m => m.ConversationId == id).OrderBy(m => m.Seq).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProtonStoredMessage>> LastMessagesAsync(Guid id, int count, CancellationToken cancellationToken = default)
        => (await db.ProtonConversationMessages.AsNoTracking()
                .Where(m => m.ConversationId == id)
                .OrderByDescending(m => m.Seq)
                .Take(count)
                .ToListAsync(cancellationToken))
            .OrderBy(m => m.Seq)
            .ToList();

    public async Task<bool> DeleteAsync(long userId, Guid id, CancellationToken cancellationToken = default)
        => await db.ProtonConversations.Where(c => c.Id == id && c.UserId == userId).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<Guid>> UntouchedSinceAsync(DateTime before, int take, CancellationToken cancellationToken = default)
        => await db.ProtonConversations.AsNoTracking()
            .Where(c => c.UpdatedAt < before)
            .OrderBy(c => c.UpdatedAt)
            .Select(c => c.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> DeleteManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => db.ProtonConversations.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken);

    public Task<int> ClearUsageTextAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => db.ProtonUsage
            .Where(u => ids.Contains(u.ConversationId) && u.VoiceSeconds == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Question, DeletedText).SetProperty(u => u.Answer, (string?)null), cancellationToken);
}
