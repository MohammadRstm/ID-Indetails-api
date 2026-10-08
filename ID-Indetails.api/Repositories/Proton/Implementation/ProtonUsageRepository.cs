using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Proton.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Proton.Implementation;

public sealed class ProtonUsageRepository(IdIndetailsDbContext db) : IProtonUsageRepository
{
    public void Add(ProtonUsage usage) => db.ProtonUsage.Add(usage);

    public void Add(ProtonBudgetTopUp topUp) => db.ProtonBudgetTopUps.Add(topUp);

    public async Task<decimal> SpentAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        => await db.ProtonUsage
            .Where(u => u.OccurredAt >= fromUtc && u.OccurredAt < toUtc)
            .SumAsync(u => (decimal?)u.CostUsd, cancellationToken) ?? 0m;

    public Task<int> QuestionsAsync(long userId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        => db.ProtonUsage.CountAsync(u => u.UserId == userId && u.OccurredAt >= fromUtc && u.OccurredAt < toUtc && u.ToolCalls > 0, cancellationToken);

    public async Task<decimal> TopUpTotalAsync(DateOnly month, CancellationToken cancellationToken = default)
        => await db.ProtonBudgetTopUps.Where(t => t.Month == month).SumAsync(t => (decimal?)t.AmountUsd, cancellationToken) ?? 0m;

    public async Task<IReadOnlyList<ProtonTopUpRow>> TopUpsAsync(DateOnly month, CancellationToken cancellationToken = default)
    {
        var rows = await db.ProtonBudgetTopUps.AsNoTracking()
            .Where(t => t.Month == month)
            .OrderBy(t => t.AddedAt)
            .Select(t => new { t.Id, t.AmountUsd, t.Note, t.AddedBy.FirstName, t.AddedBy.LastName, t.AddedBy.Email, t.AddedAt })
            .ToListAsync(cancellationToken);
        return rows.Select(t => new ProtonTopUpRow(t.Id, t.AmountUsd, t.Note, PersonName.Of(t.FirstName, t.LastName, t.Email), t.AddedAt)).ToList();
    }

    public async Task<IReadOnlyList<ProtonTurnRow>> TurnsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        => await db.ProtonUsage.AsNoTracking()
            .Where(u => u.OccurredAt >= fromUtc && u.OccurredAt < toUtc)
            .OrderBy(u => u.OccurredAt)
            .Select(u => new ProtonTurnRow(u.UserId, u.User.DomainCode, u.ConversationId, u.OccurredAt, u.CostUsd, u.Tools))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProtonUserUsage>> ByUserAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var rows = await db.ProtonUsage.AsNoTracking()
            .Where(u => u.OccurredAt >= fromUtc && u.OccurredAt < toUtc)
            .GroupBy(u => u.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Questions = g.Count(),
                Cost = g.Sum(u => u.CostUsd),
                Tokens = g.Sum(u => u.InputTokens + u.OutputTokens + u.CacheReadTokens + u.CacheWriteTokens),
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(r => r.UserId).ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToDictionaryAsync(u => u.Id, u => PersonName.Of(u.FirstName, u.LastName, u.Email), cancellationToken);

        return rows
            .Select(r => new ProtonUserUsage(r.UserId, names.GetValueOrDefault(r.UserId, "Unknown"), r.Questions, r.Cost, r.Tokens))
            .OrderByDescending(r => r.CostUsd)
            .ToList();
    }
}
