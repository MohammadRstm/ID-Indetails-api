using ID_Indetails.api.Business.Requirements;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Requirements.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Requirements.Implementation;

public sealed class RequirementRepository(IdIndetailsDbContext db) : IRequirementRepository
{
    public async Task<IReadOnlyList<ProjectRequirement>> ListAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.ProjectRequirements
            .AsNoTracking()
            .Include(r => r.Assignees).ThenInclude(a => a.User)
            .Where(r => r.ProjectId == projectId && r.DeletedAt == null)
            .OrderBy(r => r.IsDone)
            .ThenBy(r => r.IsDone ? DateTime.MinValue : r.CreatedAt)
            .ThenByDescending(r => r.DoneAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProjectRequirement>> AssignedToAsync(
        long userId,
        IReadOnlyCollection<string> domains,
        CancellationToken cancellationToken = default)
    {
        var codes = domains.ToList();
        return await db.ProjectRequirements
            .AsNoTracking()
            .Include(r => r.Project)
            .Include(r => r.Assignees).ThenInclude(a => a.User)
            .Where(r => r.DeletedAt == null
                && r.Assignees.Any(a => a.UserId == userId)
                && (r.Project.Status == ProjectStatus.Active || r.Project.Status == ProjectStatus.Hold)
                && codes.Contains(r.Project.DomainCode))
            .ToListAsync(cancellationToken);
    }

    public Task<ProjectRequirement?> GetForUpdateAsync(long projectId, long requirementId, CancellationToken cancellationToken = default)
        => db.ProjectRequirements
            .Include(r => r.Assignees)
            .SingleOrDefaultAsync(r => r.Id == requirementId && r.ProjectId == projectId && r.DeletedAt == null, cancellationToken);

    public async Task<IReadOnlyList<RequirementRules.Person>> GetPeopleAsync(
        IReadOnlyCollection<long> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.ToList();
        return await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new RequirementRules.Person(u.Id, u.RoleCode, u.DomainCode, u.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<long, string>> GetNamesAsync(
        IReadOnlyCollection<long> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.ToList();
        var users = await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } name ? name : u.Email);
    }

    public async Task<(int Total, int Done)> CountAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var counts = await db.ProjectRequirements
            .Where(r => r.ProjectId == projectId && r.DeletedAt == null)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Done = g.Count(r => r.IsDone) })
            .SingleOrDefaultAsync(cancellationToken);

        return counts is null ? (0, 0) : (counts.Total, counts.Done);
    }

    public Task AutoTickOpenAsync(long projectId, DateTime doneAt, long doneById, CancellationToken cancellationToken = default)
        => db.ProjectRequirements
            .Where(r => r.ProjectId == projectId && r.DeletedAt == null && !r.IsDone)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsDone, true)
                .SetProperty(r => r.AutoDone, true)
                .SetProperty(r => r.DoneAt, doneAt)
                .SetProperty(r => r.DoneById, doneById), cancellationToken);

    public Task UntickAutoDoneAsync(long projectId, CancellationToken cancellationToken = default)
        => db.ProjectRequirements
            .Where(r => r.ProjectId == projectId && r.DeletedAt == null && r.AutoDone)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsDone, false)
                .SetProperty(r => r.AutoDone, false)
                .SetProperty(r => r.DoneAt, (DateTime?)null)
                .SetProperty(r => r.DoneById, (long?)null), cancellationToken);

    public async Task<IReadOnlyList<ProjectRequirement>> OpenDueByAsync(string domain, DateOnly to, CancellationToken cancellationToken = default)
        => await db.ProjectRequirements
            .AsNoTracking()
            .Include(r => r.Project)
            .Include(r => r.Assignees).ThenInclude(a => a.User)
            .Where(r => r.DeletedAt == null && !r.IsDone && r.Deadline != null && r.Deadline <= to
                && r.Project.DomainCode == domain
                && (r.Project.Status == ProjectStatus.Active || r.Project.Status == ProjectStatus.Hold))
            .OrderBy(r => r.Deadline)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RequirementDeadline>> DueBetweenAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => await db.ProjectRequirements
            .AsNoTracking()
            .Where(r => r.DeletedAt == null && !r.IsDone && r.Deadline >= from && r.Deadline <= to && r.Project.Status == ProjectStatus.Active)
            .Select(r => new RequirementDeadline(
                r.Id,
                r.ProjectId,
                r.Project.Title,
                r.Project.DomainCode,
                r.Text,
                r.Deadline!.Value,
                r.Assignees.Select(a => a.UserId).ToList()))
            .ToListAsync(cancellationToken);

    public void Add(ProjectRequirement requirement) => db.ProjectRequirements.Add(requirement);
}
