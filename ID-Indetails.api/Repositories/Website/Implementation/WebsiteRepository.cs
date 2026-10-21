using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Website.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Website.Implementation;

public sealed class WebsiteRepository(IdIndetailsDbContext db) : IWebsiteRepository
{
    public async Task<bool> PositionTakenAsync(decimal latitude, decimal longitude, PlaceOwner owner, CancellationToken cancellationToken = default)
        => await db.WebsiteProjects.AnyAsync(p => p.Latitude == latitude && p.Longitude == longitude && p.Id != owner.ProjectId, cancellationToken)
            || await db.WebsiteLocations.AnyAsync(l => l.Latitude == latitude && l.Longitude == longitude && l.Id != owner.LocationId, cancellationToken);

    public async Task<IReadOnlyList<WebsiteProject>> ProjectsAsync(bool publishedOnly, CancellationToken cancellationToken = default)
        => await db.WebsiteProjects.AsNoTracking()
            .Include(p => p.Images)
            .Where(p => !publishedOnly || p.Published)
            .OrderByDescending(p => p.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, string>> ProjectTitlesBySlugAsync(CancellationToken cancellationToken = default)
        => await db.WebsiteProjects.Select(p => new { p.Slug, p.Title }).ToDictionaryAsync(p => p.Slug, p => p.Title, cancellationToken);

    public Task<WebsiteProject?> ProjectAsync(long id, CancellationToken cancellationToken = default)
        => db.WebsiteProjects.Include(p => p.Images).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<WebsiteProject?> PublishedProjectAsync(string slug, CancellationToken cancellationToken = default)
        => db.WebsiteProjects.AsNoTracking().Include(p => p.Images).SingleOrDefaultAsync(p => p.Slug == slug && p.Published, cancellationToken);

    public async Task<IReadOnlyList<WebsiteProject>> ConveyorAsync(CancellationToken cancellationToken = default)
        => await db.WebsiteProjects.Where(p => p.ConveyorOrder != null).OrderBy(p => p.ConveyorOrder).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WebsiteProject>> ProjectsByIdAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default)
        => await db.WebsiteProjects.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);

    public Task<bool> SlugTakenAsync(string slug, long? exceptId, CancellationToken cancellationToken = default)
        => db.WebsiteProjects.AnyAsync(p => p.Slug == slug && p.Id != exceptId, cancellationToken);

    public Task<long?> LinkedToAsync(long projectId, CancellationToken cancellationToken = default)
        => db.WebsiteProjects.Where(p => p.ProjectId == projectId).Select(p => (long?)p.Id).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<long, SystemProjectFacts>> SystemProjectsAsync(
        IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default)
        => ids.Count == 0
            ? new Dictionary<long, SystemProjectFacts>()
            : await Facts(db.Projects.Where(p => ids.Contains(p.Id))).ToDictionaryAsync(f => f.Id, cancellationToken);

    public async Task<IReadOnlyList<SystemProjectFacts>> CandidatesAsync(
        IReadOnlyCollection<string> domains, string? search, int take, CancellationToken cancellationToken = default)
    {
        var projects = db.Projects.Where(p => domains.Contains(p.DomainCode));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            projects = projects.Where(p => p.Title.Contains(term));
        }

        return await Facts(projects.OrderByDescending(p => p.StartDate).ThenByDescending(p => p.Id).Take(take)).ToListAsync(cancellationToken);
    }

    public async Task<bool> IsPublicFileAsync(long fileId, CancellationToken cancellationToken = default)
        => await db.CompanySettings.AnyAsync(s => s.WebsiteFaviconFileId == fileId, cancellationToken)
            || await db.WebsiteProjects.AnyAsync(
                p => p.Published && (p.ThumbnailFileId == fileId || p.HeroFileId == fileId || p.Images.Any(i => i.FileId == fileId)),
                cancellationToken);

    public Task<string?> ProjectTitleShowingAsync(long fileId, CancellationToken cancellationToken = default)
        => db.WebsiteProjects
            .Where(p => p.ThumbnailFileId == fileId || p.HeroFileId == fileId || p.Images.Any(i => i.FileId == fileId))
            .Select(p => p.Title)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<T>> ListAsync<T>(bool track = false, CancellationToken cancellationToken = default) where T : class, IWebsiteListItem
    {
        var rows = track ? db.Set<T>() : db.Set<T>().AsNoTracking();
        return rows.OrderBy(r => r.Position).ThenBy(r => r.Id).ToListAsync(cancellationToken);
    }

    public Task<T?> FindAsync<T>(long id, CancellationToken cancellationToken = default) where T : class, IWebsiteListItem
        => db.Set<T>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<int> NextPositionAsync<T>(CancellationToken cancellationToken = default) where T : class, IWebsiteListItem
        => (await db.Set<T>().MaxAsync(r => (int?)r.Position, cancellationToken) ?? 0) + 1;

    public void Add<T>(T row) where T : class => db.Set<T>().Add(row);

    public void Remove<T>(T row) where T : class => db.Set<T>().Remove(row);

    private IQueryable<SystemProjectFacts> Facts(IQueryable<Project> projects)
        => from p in projects.AsNoTracking()
           join c in db.Clients on p.ClientId equals c.Id into clients
           from c in clients.DefaultIfEmpty()
           join w in db.WebsiteProjects on p.Id equals w.ProjectId into shown
           from w in shown.DefaultIfEmpty()
           select new SystemProjectFacts(
               p.Id, p.Title, p.DomainCode, p.Status, p.CompletedAt, p.TargetDate, p.StartDate,
               c == null ? null : c.Name, p.Description, w == null ? null : (long?)w.Id);
}
