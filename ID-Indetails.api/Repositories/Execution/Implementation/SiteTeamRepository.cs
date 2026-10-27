using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Execution.Implementation;

public sealed class SiteTeamRepository(IdIndetailsDbContext db) : ISiteTeamRepository
{
    public async Task<IReadOnlyList<SiteStaffRow>> StaffAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default)
    {
        var ids = projectIds.ToList();
        var rows = await db.ProjectStaff.AsNoTracking()
            .Where(s => ids.Contains(s.ProjectId) && s.RemovedAt == null)
            .OrderBy(s => s.AssignedAt).ThenBy(s => s.Id)
            .Select(s => new { s.ProjectId, s.UserId, s.User.FirstName, s.User.LastName, s.User.Email })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new SiteStaffRow(r.ProjectId, r.UserId, PersonName.Of(r.FirstName, r.LastName, r.Email))).ToList();
    }

    public Task<List<ProjectStaff>> StaffForUpdateAsync(long projectId, CancellationToken cancellationToken = default)
        => db.ProjectStaff.Where(s => s.ProjectId == projectId).ToListAsync(cancellationToken);

    public Task<bool> IsOnTeamAsync(long projectId, long userId, CancellationToken cancellationToken = default)
        => db.ProjectStaff.AnyAsync(s => s.ProjectId == projectId && s.UserId == userId && s.RemovedAt == null, cancellationToken);

    public async Task<IReadOnlyList<SiteTeamRules.Candidate>> CandidatesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        var rows = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.RoleCode, u.DomainCode, u.Status })
            .ToListAsync(cancellationToken);
        return rows.Select(u => new SiteTeamRules.Candidate(u.Id, PersonName.Of(u.FirstName, u.LastName, u.Email), u.RoleCode, u.DomainCode, u.Status)).ToList();
    }

    public async Task<IReadOnlyList<StaffSiteRow>> SitesOfAsync(long userId, CancellationToken cancellationToken = default)
    {
        var rows = await db.ProjectStaff.AsNoTracking()
            .Where(s => s.UserId == userId && s.RemovedAt == null)
            .OrderByDescending(s => s.AssignedAt)
            .Select(s => new
            {
                s.ProjectId, s.Project.Title, ClientName = s.Project.Client != null ? s.Project.Client.Name : null,
                ClientOrganisation = s.Project.Client != null ? s.Project.Client.Organisation : null,
                s.Project.Status, s.Project.Location, s.AssignedAt,
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new StaffSiteRow(
            r.ProjectId, r.Title, r.ClientName is { } name ? ClientRules.DisplayName(name, r.ClientOrganisation) : null, r.Status, r.Location, r.AssignedAt)).ToList();
    }

    public void AddStaff(ProjectStaff staff) => db.ProjectStaff.Add(staff);

    public async Task<(IReadOnlyList<SiteLogRow> Items, int TotalCount)> SearchLogsAsync(SiteLogSearch search, CancellationToken cancellationToken = default)
    {
        var query = db.SiteLogs.AsNoTracking().Where(l => search.Domains.Contains(l.Project.DomainCode));
        if (search.UserId is { } userId)
        {
            query = query.Where(l => l.UserId == userId);
        }

        if (search.ProjectId is { } projectId)
        {
            query = query.Where(l => l.ProjectId == projectId);
        }

        if (search.FromUtc is { } from)
        {
            query = query.Where(l => l.CreatedAt >= from);
        }

        if (search.ToUtc is { } to)
        {
            query = query.Where(l => l.CreatedAt < to);
        }

        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            foreach (var word in search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                query = query.Where(l => l.Body.Contains(word) || l.Project.Title.Contains(word));
            }
        }

        var total = await query.CountAsync(cancellationToken);
        var page = await Rows(query.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize), cancellationToken);
        return (page, total);
    }

    public async Task<SiteLogRow?> GetLogAsync(long id, CancellationToken cancellationToken = default)
        => (await Rows(db.SiteLogs.AsNoTracking().Where(l => l.Id == id), cancellationToken)).SingleOrDefault();

    public Task<SiteLog?> GetLogForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.SiteLogs.Include(l => l.Project).SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

    public void AddLog(SiteLog log) => db.SiteLogs.Add(log);

    private static async Task<IReadOnlyList<SiteLogRow>> Rows(IQueryable<SiteLog> logs, CancellationToken cancellationToken)
    {
        var rows = await logs
            .Select(l => new { Log = l, l.Project.Title, l.User.FirstName, l.User.LastName, l.User.Email })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new SiteLogRow(r.Log, r.Title, PersonName.Of(r.FirstName, r.LastName, r.Email))).ToList();
    }
}
