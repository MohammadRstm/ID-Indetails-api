using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Projects.Implementation;

public sealed class ProjectRepository(IdIndetailsDbContext db) : IProjectRepository
{
    public async Task<(IReadOnlyList<Project> Items, int TotalCount)> SearchAsync(
        ProjectSearch search,
        CancellationToken cancellationToken = default)
    {
        var domains = search.Domains.ToList();
        var query = db.Projects.AsNoTracking().Where(p => domains.Contains(p.DomainCode));

        if (search.Status is { } status)
        {
            query = query.Where(p => p.Status == status);
        }

        if (search.ClientId is { } clientId)
        {
            query = query.Where(p => p.ClientId == clientId);
        }

        if (search.BillingType is { } billingType)
        {
            query = query.Where(p => p.BillingType == billingType);
        }

        if (search.Domain is { } domain)
        {
            query = query.Where(p => p.DomainCode == domain);
        }

        if (search.OwnerId is { } ownerId)
        {
            query = query.Where(p => p.OwnerId == ownerId);
        }

        if (search.OpenDuring is { } period)
        {
            query = query.Where(p => p.StartDate <= period.To && (p.CompletedAt == null || p.CompletedAt >= period.FromUtc));
        }

        foreach (var word in (search.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            query = query.Where(p => p.Title.Contains(word));
        }

        var total = await query.CountAsync(cancellationToken);
        var live = db.WorkEntries.Where(e => e.DeletedAt == null);
        var ordered = search.Sort switch
        {
            ProjectSort.Activity => query.OrderByDescending(p => live.Where(e => e.ProjectId == p.Id).Max(e => (DateTime?)e.StartedAt) ?? p.CreatedAt),
            ProjectSort.Hours => query.OrderByDescending(p => live.Where(e => e.ProjectId == p.Id).Sum(e => (int?)e.DurationMinutes) ?? 0),
            ProjectSort.Progress => query.OrderByDescending(p => p.ProgressPct),
            ProjectSort.Name => query.OrderBy(p => p.Title),
            ProjectSort.Client => query.OrderBy(p => p.Client == null).ThenBy(p => p.Client!.Organisation ?? p.Client!.Name),
            _ => query.OrderByDescending(p => p.StartDate),
        };

        var items = await ordered
            .ThenByDescending(p => p.Id)
            .Include(p => p.Client)
            .Include(p => p.Owner)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<long>> QuietCompletedAsync(string domain, DateTime beforeUtc, CancellationToken cancellationToken = default)
        => await db.Projects
            .AsNoTracking()
            .Where(p => p.DomainCode == domain
                && p.Status == ProjectStatus.Completed
                && p.CompletedAt != null
                && p.CompletedAt <= beforeUtc
                && !db.ActivityEvents.Any(e => e.ProjectId == p.Id && e.OccurredAt > beforeUtc))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkRow>> RecentWorkAsync(
        IReadOnlyCollection<long> projectIds,
        DateTime fromUtc,
        CancellationToken cancellationToken = default)
        => await db.WorkEntries
            .AsNoTracking()
            .Where(e => projectIds.Contains(e.ProjectId) && e.DeletedAt == null && e.StartedAt >= fromUtc)
            .Select(e => new WorkRow(e.Id, e.UserId, e.ProjectId, e.StartedAt, e.EndedAt, e.DurationMinutes, e.CreatedAt))
            .ToListAsync(cancellationToken);

    public Task<Project?> GetAsync(long id, CancellationToken cancellationToken = default)
        => db.Projects
            .AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.Owner)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Project?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => (await db.Projects
            .FromSql($"SELECT * FROM `projects` WHERE `id` = {id} FOR UPDATE")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

    public async Task<bool> HasBillingRecordsAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.Invoices.AnyAsync(i => i.ProjectId == projectId, cancellationToken)
            || await db.Payments.AnyAsync(p => p.ProjectId == projectId, cancellationToken);

    public void Add(Project project) => db.Projects.Add(project);
}
