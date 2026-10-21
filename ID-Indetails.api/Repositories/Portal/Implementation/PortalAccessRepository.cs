using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Portal.Implementation;

public sealed class PortalAccessRepository(IdIndetailsDbContext db) : IPortalAccessRepository
{
    public async Task<PortalProjectFacts?> GetProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.DomainCode, p.Title, p.Status, p.BillingType, p.ClientId })
            .SingleOrDefaultAsync(cancellationToken);
        if (project is null)
        {
            return null;
        }

        var client = project.ClientId is { } clientId
            ? await db.Clients.AsNoTracking()
                .Where(c => c.Id == clientId)
                .Select(c => new
                {
                    c.ArchivedAt,
                    Emails = c.Contacts
                        .Where(x => x.Kind == ContactKind.Email)
                        .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Id)
                        .Select(x => new PortalClientContact(x.NormalizedValue, x.Label))
                        .ToList(),
                })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        return new PortalProjectFacts(
            project.Id,
            project.DomainCode,
            project.Title,
            project.Status,
            project.BillingType,
            project.ClientId,
            client?.ArchivedAt is not null,
            client?.Emails ?? []);
    }

    public async Task<IReadOnlyList<long>> ProjectIdsOfClientAsync(long clientId, CancellationToken cancellationToken = default)
        => await db.Projects.AsNoTracking()
            .Where(p => p.ClientId == clientId)
            .OrderBy(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

    public Task<PortalProjectAccess?> GetAsync(long accountId, long projectId, CancellationToken cancellationToken = default)
        => db.PortalProjectAccess.SingleOrDefaultAsync(a => a.AccountId == accountId && a.ProjectId == projectId, cancellationToken);

    public Task<PortalProjectAccess?> GetByIdAsync(long accessId, CancellationToken cancellationToken = default)
        => db.PortalProjectAccess.Include(a => a.Account).SingleOrDefaultAsync(a => a.Id == accessId, cancellationToken);

    public async Task<IReadOnlyList<PortalProjectAccess>> ForProjectAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.PortalProjectAccess.AsNoTracking().Include(a => a.Account)
            .Where(a => a.ProjectId == projectId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PortalProjectAccess>> LiveForProjectAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.PortalProjectAccess.Include(a => a.Account)
            .Where(a => a.ProjectId == projectId && a.RevokedAt == null)
            .ToListAsync(cancellationToken);

    public Task<bool> HasLiveAccessAsync(long accountId, long exceptProjectId, CancellationToken cancellationToken = default)
        => db.PortalProjectAccess.AnyAsync(
            a => a.AccountId == accountId && a.ProjectId != exceptProjectId && a.RevokedAt == null, cancellationToken);

    public async Task<IReadOnlyList<PortalProjectSummary>> ProjectsOfAsync(long accountId, CancellationToken cancellationToken = default)
        => await db.PortalProjectAccess.AsNoTracking()
            .Where(a => a.AccountId == accountId && a.RevokedAt == null && a.Project.DomainCode == DomainCodes.Design)
            .OrderByDescending(a => a.Project.StartDate).ThenByDescending(a => a.ProjectId)
            .Select(a => new PortalProjectSummary(
                a.Project.Id,
                a.Project.Title,
                a.Project.Status,
                a.Project.StartDate,
                a.LastOpenedAt,
                db.PortalSettings.Where(s => s.ProjectId == a.ProjectId).Max(s => s.PublishedAt),
                // only what this project's clients may read: nothing from before it moved to another client
                db.PortalMessages.Where(m => m.ProjectId == a.ProjectId && m.DeletedAt == null
                        && !db.PortalSettings.Any(s => s.ProjectId == a.ProjectId && s.DiscussionVisibleFrom != null && m.CreatedAt < s.DiscussionVisibleFrom))
                    .Max(m => (DateTime?)m.CreatedAt),
                db.PortalComments.Where(c => c.ProjectId == a.ProjectId && c.DeletedAt == null
                        && !db.PortalSettings.Any(s => s.ProjectId == a.ProjectId && s.DiscussionVisibleFrom != null && c.CreatedAt < s.DiscussionVisibleFrom))
                    .Max(c => (DateTime?)c.CreatedAt),
                db.PortalHeroImages
                    .Where(h => h.ProjectId == a.ProjectId && h.IsPublished)
                    .OrderBy(h => h.SortOrder).ThenBy(h => h.Id)
                    .Select(h => (long?)h.FileId)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

    public Task MarkOpenedAsync(long accountId, long projectId, DateTime openedAt, CancellationToken cancellationToken = default)
        => db.PortalProjectAccess
            .Where(a => a.AccountId == accountId && a.ProjectId == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastOpenedAt, openedAt), cancellationToken);

    public void Add(PortalProjectAccess access) => db.PortalProjectAccess.Add(access);
}
