using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.WorkEntries.Implementation;

public sealed class WorkEntryRepository(IdIndetailsDbContext db) : IWorkEntryRepository
{
    public Task LockPersonAsync(long userId, CancellationToken cancellationToken = default)
        => db.Database.ExecuteSqlAsync($"SELECT `id` FROM `users` WHERE `id` = {userId} FOR UPDATE", cancellationToken);

    public Task<WorkEntry?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.WorkEntries.Include(e => e.Project).SingleOrDefaultAsync(e => e.Id == id && e.DeletedAt == null, cancellationToken);

    public Task<WorkEntry?> GetDeletedForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.WorkEntries.Include(e => e.Project).SingleOrDefaultAsync(e => e.Id == id && e.DeletedAt != null, cancellationToken);

    public async Task<IReadOnlyList<WorkEntry>> ListOwnForUpdateAsync(
        long userId,
        IReadOnlyCollection<long> ids,
        CancellationToken cancellationToken = default)
        => await db.WorkEntries
            .Include(e => e.Project)
            .Where(e => ids.Contains(e.Id) && e.UserId == userId && e.DeletedAt == null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkEntryRules.Span>> SpansAsync(
        long userId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
        => await db.WorkEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.DeletedAt == null && e.StartedAt < to && e.EndedAt > from)
            .Select(e => new WorkEntryRules.Span(e.Id, e.StartedAt, e.EndedAt, e.Project.Title))
            .ToListAsync(cancellationToken);

    public async Task<WorkEntryView?> GetViewAsync(long id, CancellationToken cancellationToken = default)
        => (await ViewsAsync(db.WorkEntries.Where(e => e.Id == id && e.DeletedAt == null), cancellationToken)).SingleOrDefault();

    public async Task<(IReadOnlyList<WorkEntryView> Items, int TotalCount)> SearchAsync(
        WorkEntrySearch search,
        CancellationToken cancellationToken = default)
    {
        var query = Filter(search, withText: true);
        var total = await query.CountAsync(cancellationToken);
        var page = query
            .OrderByDescending(e => e.StartedAt)
            .ThenByDescending(e => e.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize);

        return (await ViewsAsync(page, cancellationToken), total);
    }

    public Task<IReadOnlyList<WorkEntryView>> ListAsync(WorkEntrySearch search, CancellationToken cancellationToken = default)
        => ViewsAsync(Filter(search, withText: true).OrderBy(e => e.StartedAt).ThenBy(e => e.Id), cancellationToken);

    public async Task<IReadOnlyList<WorkRow>> RowsAsync(WorkEntrySearch search, CancellationToken cancellationToken = default)
        => await Filter(search, withText: false)
            .Select(e => new WorkRow(e.Id, e.UserId, e.ProjectId, e.StartedAt, e.EndedAt, e.DurationMinutes, e.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkProject>> OpenProjectsAsync(CancellationToken cancellationToken = default)
        => await db.Projects
            .AsNoTracking()
            .Where(p => p.DomainCode == DomainCodes.Design && (p.Status == ProjectStatus.Active || p.Status == ProjectStatus.Hold))
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Select(p => new WorkProject(
                p.Id, p.Title, p.Description, p.Client != null ? p.Client.Name : null, p.Status, p.ProgressPct, p.StartDate, p.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Contribution>> ContributionsAsync(
        IReadOnlyCollection<long> projectIds,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.WorkEntries
            .AsNoTracking()
            .Where(e => projectIds.Contains(e.ProjectId) && e.DeletedAt == null)
            .GroupBy(e => new { e.ProjectId, e.UserId })
            .Select(g => new
            {
                g.Key.ProjectId,
                g.Key.UserId,
                Minutes = g.Sum(e => e.DurationMinutes),
                Entries = g.Count(),
                LastStartedAt = g.Max(e => e.StartedAt),
            })
            .ToListAsync(cancellationToken);

        var names = (await PeopleAsync(rows.Select(r => r.UserId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        return rows
            .Select(r => new Contribution(r.ProjectId, r.UserId, names.GetValueOrDefault(r.UserId, ""), r.Minutes, r.Entries, r.LastStartedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<DesignPerson>> DesignPeopleAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Users
            .AsNoTracking()
            .Where(u => u.DomainCode == DomainCodes.Design && u.Status != UserStatus.Deactivated)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.Position, u.TeamId, TeamName = u.Team != null ? u.Team.Name : null, u.RoleCode, u.Status })
            .ToListAsync(cancellationToken);

        return rows
            .Select(u => new DesignPerson(u.Id, PersonName.Of(u.FirstName, u.LastName, u.Email), u.Email, u.Position, u.TeamId, u.TeamName, u.RoleCode, u.Status))
            .OrderBy(p => p.Name)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<long, string>> ProjectTitlesAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default)
        => await db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);

    public async Task<IReadOnlyList<WorkPerson>> PeopleAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default)
    {
        var rows = await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, TeamName = u.Team != null ? u.Team.Name : null, u.DomainCode })
            .ToListAsync(cancellationToken);

        return rows
            .Select(u => new WorkPerson(u.Id, PersonName.Of(u.FirstName, u.LastName, u.Email), u.Email, u.TeamName, u.DomainCode))
            .ToList();
    }

    public async Task<IReadOnlyList<long>> PeopleWhoMayHoldAsync(
        string domainCode,
        string permissionCode,
        CancellationToken cancellationToken = default)
        => await db.Users
            .AsNoTracking()
            .Where(u => u.Status == UserStatus.Active
                && (u.DomainCode == domainCode
                    || u.RoleCode == RoleCodes.SuperAdmin
                    || db.UserPermissionOverrides.Any(o => o.UserId == u.Id && o.PermissionCode == permissionCode && o.Effect == PermissionEffect.Grant)))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

    public Task LockDomainAsync(string domainCode, CancellationToken cancellationToken = default)
        => db.Database.ExecuteSqlAsync($"SELECT `code` FROM `domains` WHERE `code` = {domainCode} FOR UPDATE", cancellationToken);

    public void Add(WorkEntry entry) => db.WorkEntries.Add(entry);

    private IQueryable<WorkEntry> Filter(WorkEntrySearch search, bool withText)
    {
        var query = db.WorkEntries.AsNoTracking().Where(e => e.DeletedAt == null && e.Project.DomainCode == DomainCodes.Design);
        if (search.UserId is { } userId)
        {
            query = query.Where(e => e.UserId == userId);
        }

        if (search.ProjectId is { } projectId)
        {
            query = query.Where(e => e.ProjectId == projectId);
        }

        if (search.ClientId is { } clientId)
        {
            query = query.Where(e => e.Project.ClientId == clientId);
        }

        if (search.From is { } from)
        {
            query = query.Where(e => e.StartedAt >= from);
        }

        if (search.To is { } to)
        {
            query = query.Where(e => e.StartedAt < to);
        }

        if (withText && !string.IsNullOrWhiteSpace(search.Text))
        {
            foreach (var word in search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                query = query.Where(e => e.Description.Contains(word) || e.Project.Title.Contains(word));
            }
        }

        return query;
    }

    private sealed record Raw(
        long Id, long UserId, string? FirstName, string? LastName, string Email, long ProjectId, string ProjectTitle,
        ProjectStatus ProjectStatus, string ProjectDomain, long? ClientId, string? ClientName, string Description,
        DateTime StartedAt, DateTime EndedAt, int Minutes, DateTime CreatedAt, DateTime? UpdatedAt, int CommentCount);

    /// <summary>Loads the entries of an already ordered and paged query, with the details shown for each.</summary>
    private static async Task<IReadOnlyList<WorkEntryView>> ViewsAsync(IQueryable<WorkEntry> entries, CancellationToken cancellationToken)
    {
        var rows = await entries.AsNoTracking().Select(e => new Raw(
            e.Id, e.UserId, e.User.FirstName, e.User.LastName, e.User.Email, e.ProjectId, e.Project.Title,
            e.Project.Status, e.Project.DomainCode, e.Project.ClientId, e.Project.Client != null ? e.Project.Client.Name : null,
            e.Description, e.StartedAt, e.EndedAt, e.DurationMinutes, e.CreatedAt, e.UpdatedAt, e.Comments.Count()))
            .ToListAsync(cancellationToken);

        return rows.Select(r => new WorkEntryView(
            r.Id, r.UserId, PersonName.Of(r.FirstName, r.LastName, r.Email), r.ProjectId, r.ProjectTitle, r.ProjectStatus,
            r.ProjectDomain, r.ClientId, r.ClientName, r.Description, r.StartedAt, r.EndedAt, r.Minutes, r.CreatedAt,
            r.UpdatedAt, r.CommentCount)).ToList();
    }
}
