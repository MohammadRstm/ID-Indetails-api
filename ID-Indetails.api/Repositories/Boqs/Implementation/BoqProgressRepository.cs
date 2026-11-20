using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Boqs.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Boqs.Implementation;

public sealed class BoqProgressRepository(IdIndetailsDbContext db) : IBoqProgressRepository
{
    public async Task<IReadOnlyList<BoqProgressLineRow>> AgreedLinesAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var rows = await db.BoqLines
            .AsNoTracking()
            .Where(l => l.Boq.ProjectId == projectId && l.Boq.DeletedAt == null && l.Boq.Status == BoqStatus.Agreed && l.Boq.Kind != BoqKind.Internal)
            .Select(l => new
            {
                l.Id,
                l.BoqId,
                l.Boq.Kind,
                l.Boq.Status,
                l.Boq.Number,
                l.Boq.Title,
                l.Boq.SubtotalUsd,
                l.Boq.TotalUsd,
                l.Boq.AgreedAt,
                l.Position,
                l.Nature,
                l.Code,
                l.Description,
                l.Unit,
                l.Quantity,
                l.WasteRate,
                l.Rate,
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.Kind == BoqKind.Client ? 0 : 1)
            .ThenBy(r => r.AgreedAt)
            .ThenBy(r => r.BoqId)
            .ThenBy(r => r.Position)
            .ThenBy(r => r.Id)
            .Select(r => new BoqProgressLineRow(
                new BoqProgressRules.Line(r.Id, r.BoqId, r.Kind, r.Status, r.Title, r.SubtotalUsd, r.TotalUsd, r.Nature, r.Quantity, r.WasteRate, r.Rate),
                r.Number,
                r.AgreedAt,
                r.Position,
                r.Code,
                r.Description,
                r.Unit))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<long, BoqLineProgress>> LatestAsync(long projectId, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var latest = db.BoqLineProgress
            .Where(p => p.ProjectId == projectId && p.RecordedOn <= asOf)
            .GroupBy(p => p.BoqLineId)
            .Select(g => new { Line = g.Key, Day = g.Max(p => p.RecordedOn) });

        // one record per line and day, so the line's latest day is one record
        var records = await db.BoqLineProgress
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .Join(latest, p => new { Line = p.BoqLineId, Day = p.RecordedOn }, l => new { l.Line, l.Day }, (p, _) => p)
            .ToListAsync(cancellationToken);
        return records.ToDictionary(r => r.BoqLineId);
    }

    public async Task<IReadOnlyList<BoqProgressRecordRow>> HistoryAsync(IReadOnlyCollection<long> lineIds, DateOnly asOf, int take, CancellationToken cancellationToken = default)
    {
        var ids = lineIds.ToList();
        var rows = await db.BoqLineProgress
            .AsNoTracking()
            .Where(p => ids.Contains(p.BoqLineId) && p.RecordedOn <= asOf)
            .Join(db.Users, p => p.RecordedById, u => u.Id, (p, u) => new { Record = p, u.FirstName, u.LastName, u.Email })
            .OrderByDescending(r => r.Record.RecordedOn)
            .ThenByDescending(r => r.Record.RecordedAt)
            .ThenByDescending(r => r.Record.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(r => new BoqProgressRecordRow(r.Record, PersonName.Of(r.FirstName, r.LastName, r.Email))).ToList();
    }

    public async Task<IReadOnlyList<BoqLineProgress>> OnDayForUpdateAsync(IReadOnlyCollection<long> lineIds, DateOnly day, CancellationToken cancellationToken = default)
    {
        var ids = lineIds.ToList();
        return await db.BoqLineProgress.Where(p => ids.Contains(p.BoqLineId) && p.RecordedOn == day).ToListAsync(cancellationToken);
    }

    public void Add(BoqLineProgress record) => db.BoqLineProgress.Add(record);
}
