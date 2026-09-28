using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Labour.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Labour.Implementation;

public sealed class LabourRepository(IdIndetailsDbContext db) : ILabourRepository
{
    public Task<IReadOnlyList<LabourWeekRecord>> ListWeeksAsync(long projectId, CancellationToken cancellationToken = default)
        => WeeksAsync(db.LabourWeeks.Where(w => w.ProjectId == projectId), cancellationToken);

    public async Task<LabourWeekRecord?> GetWeekAsync(long projectId, DateOnly weekStart, CancellationToken cancellationToken = default)
        => (await WeeksAsync(db.LabourWeeks.Where(w => w.ProjectId == projectId && w.WeekStart == weekStart), cancellationToken))
            .SingleOrDefault();

    public Task<LabourWeek?> GetWeekForUpdateAsync(long projectId, DateOnly weekStart, CancellationToken cancellationToken = default)
        => db.LabourWeeks
            .Include(w => w.Days)
            .SingleOrDefaultAsync(w => w.ProjectId == projectId && w.WeekStart == weekStart, cancellationToken);

    public void AddWeek(LabourWeek week) => db.LabourWeeks.Add(week);

    public void RemoveDay(LabourWeekDay day) => db.LabourWeekDays.Remove(day);

    public async Task<IReadOnlyList<LabourDefaultRecord>> ListDefaultsAsync(long projectId, CancellationToken cancellationToken = default)
        => await (
                from d in db.ProjectLabourDefaults.AsNoTracking()
                where d.ProjectId == projectId
                join u in db.Users on d.CreatedById equals u.Id into setters
                from u in setters.DefaultIfEmpty()
                select new LabourDefaultRecord(d, u == null ? null : (u.FirstName + " " + u.LastName).Trim()))
            .ToListAsync(cancellationToken);

    public Task<ProjectLabourDefault?> GetDefaultForUpdateAsync(long projectId, DateOnly effectiveFrom, CancellationToken cancellationToken = default)
        => db.ProjectLabourDefaults.SingleOrDefaultAsync(d => d.ProjectId == projectId && d.EffectiveFrom == effectiveFrom, cancellationToken);

    public void AddDefault(ProjectLabourDefault labourDefault) => db.ProjectLabourDefaults.Add(labourDefault);

    private async Task<IReadOnlyList<LabourWeekRecord>> WeeksAsync(IQueryable<LabourWeek> weeks, CancellationToken cancellationToken)
    {
        var rows = await weeks.AsNoTracking().Include(w => w.Days).ToListAsync(cancellationToken);
        var ids = rows.Select(w => w.SubmittedById).Distinct().ToList();
        var names = await db.Users
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);
        return rows.Select(w => new LabourWeekRecord(w, names[w.SubmittedById])).ToList();
    }
}
