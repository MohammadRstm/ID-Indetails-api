using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Reports.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Reports.Implementation;

public sealed class ReportRepository(IdIndetailsDbContext db) : IReportRepository
{
    public async Task<IReadOnlyList<Project>> DesignProjectsAsync(CancellationToken cancellationToken = default)
        => await db.Projects
            .AsNoTracking()
            .Include(p => p.Client)
            .Where(p => p.DomainCode == DomainCodes.Design)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<long, DateTime>> HeldSinceAsync(
        IReadOnlyCollection<long> projectIds,
        CancellationToken cancellationToken = default)
        => await db.ActivityEvents
            .AsNoTracking()
            .Where(e => e.Kind == ActivityEventKinds.ProjectHold && e.ProjectId != null && projectIds.Contains(e.ProjectId.Value))
            .GroupBy(e => e.ProjectId!.Value)
            .Select(g => new { ProjectId = g.Key, Since = g.Max(e => e.OccurredAt) })
            .ToDictionaryAsync(r => r.ProjectId, r => r.Since, cancellationToken);
}
