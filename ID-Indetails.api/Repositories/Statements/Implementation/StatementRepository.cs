using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Statements.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Statements.Implementation;

public sealed class StatementRepository(IdIndetailsDbContext db) : IStatementRepository
{
    public async Task<(IReadOnlyList<StatementListRow> Items, int TotalCount)> ListAsync(
        long projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = db.ClientStatements.AsNoTracking().Where(s => s.ProjectId == projectId);
        var total = await query.CountAsync(cancellationToken);
        var statements = await query
            .Include(s => s.Deliveries)
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var ids = statements.Select(s => s.Id).ToList();
        var totals = await db.ClientStatementLines
            .Where(l => ids.Contains(l.StatementId))
            .GroupBy(l => l.StatementId)
            .Select(g => new { StatementId = g.Key, Count = g.Count(), Total = g.Sum(l => l.Price) })
            .ToDictionaryAsync(x => x.StatementId, cancellationToken);

        return (statements.Select(s => new StatementListRow(s, totals[s.Id].Count, totals[s.Id].Total)).ToList(), total);
    }

    public Task<ClientStatement?> GetAsync(long id, CancellationToken cancellationToken = default)
        => db.ClientStatements
            .AsNoTracking()
            .Include(s => s.Project)
            .Include(s => s.Client)
            .Include(s => s.Lines)
            .Include(s => s.Deliveries)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<DateTime?> LatestCreatedAtAsync(long projectId, CancellationToken cancellationToken = default)
        => db.ClientStatements
            .Where(s => s.ProjectId == projectId)
            .MaxAsync(s => (DateTime?)s.CreatedAt, cancellationToken);

    public async Task<int> PeekSequenceAsync(int year, CancellationToken cancellationToken = default)
        => (await db.StatementSequences.Where(s => s.Year == year).Select(s => (int?)s.LastNumber).SingleOrDefaultAsync(cancellationToken) ?? 0) + 1;

    public async Task<int> NextSequenceAsync(int year, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO `statement_sequences` (`year`, `last_number`) VALUES ({year}, 1) ON DUPLICATE KEY UPDATE `last_number` = `last_number` + 1",
            cancellationToken);

        return await db.Database
            .SqlQuery<int>($"SELECT `last_number` AS `Value` FROM `statement_sequences` WHERE `year` = {year}")
            .SingleAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<long, string>> NamesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
        => await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

    public void Add(ClientStatement statement) => db.ClientStatements.Add(statement);

    public void AddDelivery(ClientStatementDelivery delivery) => db.ClientStatementDeliveries.Add(delivery);
}
