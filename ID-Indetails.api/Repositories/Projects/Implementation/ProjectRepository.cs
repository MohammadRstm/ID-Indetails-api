using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Projects.Abstraction;
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

        foreach (var word in (search.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            query = query.Where(p => p.Title.Contains(word));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(p => p.Client)
            .Include(p => p.Owner)
            .OrderByDescending(p => p.StartDate)
            .ThenByDescending(p => p.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

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
