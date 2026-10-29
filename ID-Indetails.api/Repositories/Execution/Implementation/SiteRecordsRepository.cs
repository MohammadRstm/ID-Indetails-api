using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Execution.Implementation;

public sealed class SiteRecordsRepository(IdIndetailsDbContext db) : ISiteRecordsRepository
{
    public async Task<IReadOnlyList<PersonMaterialRow>> MaterialsByAsync(
        long userId,
        IReadOnlyCollection<string> domains,
        CancellationToken cancellationToken = default)
        => await db.SiteExpenses
            .AsNoTracking()
            .Where(m => m.AddedById == userId && m.DeletedAt == null && domains.Contains(m.Project.DomainCode))
            .Select(m => new PersonMaterialRow(m.Id, m.ProjectId, m.Name, m.Total, m.Currency, m.SpentOn, m.AddedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PersonLabourWeekRow>> LabourWeeksByAsync(
        long userId,
        IReadOnlyCollection<string> domains,
        CancellationToken cancellationToken = default)
        => await db.LabourWeeks
            .AsNoTracking()
            .Where(w => w.SubmittedById == userId && domains.Contains(w.Project.DomainCode))
            .Select(w => new PersonLabourWeekRow(w.Id, w.ProjectId, w.WeekStart, w.Days.Sum(d => d.Cost), w.SubmittedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Project>> SitesAsync(
        IReadOnlyCollection<long> projectIds,
        IReadOnlyCollection<string> domains,
        CancellationToken cancellationToken = default)
        => await db.Projects
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.Id) && p.DomainCode == DomainCodes.Execution && domains.Contains(p.DomainCode))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SiteActivityRow>> ActivityAsync(
        IReadOnlyCollection<long> projectIds,
        long userId,
        CancellationToken cancellationToken = default)
        => await db.Projects
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .Select(p => new SiteActivityRow(
                p.Id,
                db.SiteExpenses.Count(m => m.ProjectId == p.Id && m.DeletedAt == null),
                db.LabourWeeks.Count(w => w.ProjectId == p.Id),
                db.SiteExpenses.Where(m => m.ProjectId == p.Id && m.AddedById == userId && m.DeletedAt == null).Max(m => (DateTime?)m.AddedAt),
                db.LabourWeeks.Where(w => w.ProjectId == p.Id && w.SubmittedById == userId).Max(w => (DateTime?)w.SubmittedAt)))
            .ToListAsync(cancellationToken);
}
