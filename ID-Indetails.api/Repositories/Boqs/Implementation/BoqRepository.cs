using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Boqs.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Boqs.Implementation;

public sealed class BoqRepository(IdIndetailsDbContext db) : IBoqRepository
{
    public async Task<IReadOnlyList<BoqListRow>> ListAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var boqs = await db.Boqs
            .AsNoTracking()
            .Where(b => b.ProjectId == projectId && b.DeletedAt == null)
            .Include(b => b.Deliveries)
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .ToListAsync(cancellationToken);

        var counts = await db.BoqLines
            .Where(l => l.Boq.ProjectId == projectId && l.Boq.DeletedAt == null)
            .GroupBy(l => l.BoqId)
            .Select(g => new { BoqId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BoqId, x => x.Count, cancellationToken);

        return boqs.Select(b => new BoqListRow(b, counts.GetValueOrDefault(b.Id))).ToList();
    }

    public Task<Boq?> GetAsync(long id, CancellationToken cancellationToken = default)
        => db.Boqs
            .AsNoTracking()
            .Include(b => b.Project).ThenInclude(p => p.Client)
            .Include(b => b.Lines)
            .Include(b => b.Deliveries)
            .AsSplitQuery()
            .SingleOrDefaultAsync(b => b.Id == id && b.DeletedAt == null, cancellationToken);

    public Task<Boq?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.Boqs
            .Include(b => b.Lines)
            .Include(b => b.Deliveries)
            .AsSplitQuery()
            .SingleOrDefaultAsync(b => b.Id == id && b.DeletedAt == null, cancellationToken);

    public async Task<AgreedBoqs> AgreedAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var agreed = await db.Boqs
            .AsNoTracking()
            .Where(b => b.ProjectId == projectId && b.DeletedAt == null && b.Status == BoqStatus.Agreed)
            .Select(b => new { b.Kind, b.TotalUsd })
            .ToListAsync(cancellationToken);
        var client = agreed.Where(b => b.Kind == BoqKind.Client).ToList();

        return new AgreedBoqs(client.Count > 0, client.Sum(b => b.TotalUsd), agreed.Where(b => b.Kind == BoqKind.Vo).Select(b => b.TotalUsd).ToList());
    }

    public async Task<int> NextSequenceAsync(string prefix, int year, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO `boq_sequences` (`prefix`, `year`, `last_number`) VALUES ({prefix}, {year}, 1) ON DUPLICATE KEY UPDATE `last_number` = `last_number` + 1",
            cancellationToken);

        return await db.Database
            .SqlQuery<int>($"SELECT `last_number` AS `Value` FROM `boq_sequences` WHERE `prefix` = {prefix} AND `year` = {year}")
            .SingleAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<long, string>> NamesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
        => (await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                .ToListAsync(cancellationToken))
            .ToDictionary(u => u.Id, u => PersonName.Of(u.FirstName, u.LastName, u.Email));

    public void Add(Boq boq) => db.Boqs.Add(boq);

    public void AddDelivery(BoqDelivery delivery) => db.BoqDeliveries.Add(delivery);
}
