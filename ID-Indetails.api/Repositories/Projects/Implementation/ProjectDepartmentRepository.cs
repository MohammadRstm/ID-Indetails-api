using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Projects.Implementation;

public sealed class ProjectDepartmentRepository(IdIndetailsDbContext db) : IProjectDepartmentRepository
{
    public async Task<IReadOnlyList<ProjectDepartmentRow>> ListAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default)
    {
        var ids = projectIds.ToList();
        return await db.ProjectDepartments
            .AsNoTracking()
            .Where(d => ids.Contains(d.ProjectId))
            .OrderBy(d => d.Team.Name)
            .Select(d => new ProjectDepartmentRow(d.ProjectId, d.TeamId, d.Team.Name, d.Share))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectDepartment>> ListForUpdateAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.ProjectDepartments
            .Include(d => d.Team)
            .Where(d => d.ProjectId == projectId)
            .OrderBy(d => d.Team.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProjectDepartmentRules.Candidate>> CandidatesAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.ToList();
        return await db.Teams
            .AsNoTracking()
            .Where(t => list.Contains(t.Id))
            .Select(t => new ProjectDepartmentRules.Candidate(t.Id, t.Name, t.DomainCode))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<long>> AllDesignAsync(CancellationToken cancellationToken = default)
        => await db.Teams.AsNoTracking().Where(t => t.DomainCode == DomainCodes.Design).Select(t => t.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<long>> WithHoursAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.WorkEntries
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.DeletedAt == null && e.TeamId != null)
            .Select(e => e.TeamId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<long, string>> NamesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.ToList();
        var people = await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToListAsync(cancellationToken);
        return people.ToDictionary(p => p.Id, p => PersonName.Of(p.FirstName, p.LastName, p.Email));
    }

    public Task<int> ProjectCountAsync(long teamId, CancellationToken cancellationToken = default)
        => db.ProjectDepartments.CountAsync(d => d.TeamId == teamId, cancellationToken);

    public async Task<IReadOnlyList<ProjectDepartmentChangeRow>> ChangesAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var rows = await db.ProjectDepartmentChanges
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.ChangedAt)
            .ThenByDescending(c => c.Id)
            .Select(c => new
            {
                Change = c,
                By = db.Users.Where(u => u.Id == c.ChangedById).Select(u => new { u.FirstName, u.LastName, u.Email }).First(),
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new ProjectDepartmentChangeRow(r.Change, PersonName.Of(r.By.FirstName, r.By.LastName, r.By.Email))).ToList();
    }

    public void Add(ProjectDepartment department) => db.ProjectDepartments.Add(department);

    public void Remove(ProjectDepartment department) => db.ProjectDepartments.Remove(department);

    public void AddChange(ProjectDepartmentChange change) => db.ProjectDepartmentChanges.Add(change);
}
