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
