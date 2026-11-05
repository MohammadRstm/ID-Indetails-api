using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Projects.Implementation;

public sealed class DesignVoRepository(IdIndetailsDbContext db) : IDesignVoRepository
{
    public async Task<IReadOnlyList<DesignVo>> ListAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.DesignVos
            .AsNoTracking()
            .AsSplitQuery()
            .Include(v => v.Lines)
            .Include(v => v.Allocations).ThenInclude(a => a.Team)
            .Where(v => v.ProjectId == projectId)
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .ToListAsync(cancellationToken);

    public Task<DesignVo?> GetAsync(long id, CancellationToken cancellationToken = default)
        => db.DesignVos
            .AsNoTracking()
            .AsSplitQuery()
            .Include(v => v.Lines)
            .Include(v => v.Allocations).ThenInclude(a => a.Team)
            .Include(v => v.Project).ThenInclude(p => p.Client)
            .SingleOrDefaultAsync(v => v.Id == id, cancellationToken);

    public Task<DesignVo?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.DesignVos
            .AsSplitQuery()
            .Include(v => v.Lines)
            .Include(v => v.Allocations)
            .SingleOrDefaultAsync(v => v.Id == id, cancellationToken);

    public async Task<int> NextSequenceAsync(int year, CancellationToken cancellationToken = default)
    {
        // Design's own series in the document number table, so its numbers never wait on a BOQ's
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO `boq_sequences` (`prefix`, `year`, `last_number`) VALUES ({DesignVoRules.Series}, {year}, 1) ON DUPLICATE KEY UPDATE `last_number` = `last_number` + 1",
            cancellationToken);

        return await db.Database
            .SqlQuery<int>($"SELECT `last_number` AS `Value` FROM `boq_sequences` WHERE `prefix` = {DesignVoRules.Series} AND `year` = {year}")
            .SingleAsync(cancellationToken);
    }

    public async Task<AppliedDesignVos> AppliedAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var applied = db.DesignVos.AsNoTracking().Where(v => v.ProjectId == projectId && v.Status == DesignVoStatus.Applied);
        var total = await applied.SumAsync(v => (decimal?)v.TotalUsd, cancellationToken) ?? 0;
        var byDepartment = await db.DesignVoAllocations
            .AsNoTracking()
            .Where(a => applied.Any(v => v.Id == a.DesignVoId))
            .GroupBy(a => a.TeamId)
            .Select(g => new { TeamId = g.Key, Amount = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.TeamId, x => x.Amount, cancellationToken);
        return new AppliedDesignVos(total, byDepartment);
    }

    public Task<DateTime?> LastAppliedAtAsync(long projectId, CancellationToken cancellationToken = default)
        => db.DesignVos
            .AsNoTracking()
            .Where(v => v.ProjectId == projectId && v.Status == DesignVoStatus.Applied)
            .MaxAsync(v => v.AppliedAt, cancellationToken);

    public async Task<int> OutsideScopeMinutesAsync(long projectId, DateTime sinceUtc, CancellationToken cancellationToken = default)
        => await db.WorkEntries
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.DeletedAt == null && e.OutsideScope && e.StartedAt >= sinceUtc)
            .SumAsync(e => (int?)e.DurationMinutes, cancellationToken) ?? 0;

    public void Add(DesignVo vo) => db.DesignVos.Add(vo);
}
