using System.Text.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Payroll.Implementation;

public sealed class PayrollRepository(IdIndetailsDbContext db) : IPayrollRepository
{
    public async Task<IReadOnlyList<User>> PeopleAsync(
        IReadOnlyList<string> domains,
        bool includeSuperAdmins,
        string? domain,
        string? search,
        DateOnly employedSince,
        CancellationToken cancellationToken = default)
    {
        var codes = domains.ToList();
        var query = db.Users
            .AsNoTracking()
            .Include(u => u.Team)
            .Include(u => u.EmploymentPeriods)
            .Where(u => u.EmploymentPeriods.Any(p => p.EndedOn == null || p.EndedOn >= employedSince)
                && (u.DomainCode != null ? codes.Contains(u.DomainCode) : includeSuperAdmins));

        if (domain is not null)
        {
            query = query.Where(u => u.DomainCode == domain);
        }

        foreach (var word in (search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            query = query.Where(u =>
                (u.FirstName != null && u.FirstName.Contains(word))
                || (u.LastName != null && u.LastName.Contains(word))
                || u.Email.Contains(word));
        }

        return await query.OrderBy(u => u.DomainCode).ThenBy(u => u.FirstName).ThenBy(u => u.LastName).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserPayHistory>> PayHistoryAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
        => await db.UserPayHistory.AsNoTracking().Where(p => userIds.Contains(p.UserId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<long, string>> RolesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
        => await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.RoleCode, cancellationToken);

    public async Task<IReadOnlyList<RoleChangeRow>> RoleChangesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
    {
        string[] kinds = [ActivityEventKinds.RoleChanged, ActivityEventKinds.RoleAutoPromoted, ActivityEventKinds.RoleAutoDemoted];
        var events = await db.ActivityEvents
            .AsNoTracking()
            .Where(e => kinds.Contains(e.Kind) && e.SubjectType == "user" && e.SubjectId != null && userIds.Contains(e.SubjectId.Value) && e.Payload != null)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Select(e => new { UserId = e.SubjectId!.Value, e.OccurredAt, e.Payload })
            .ToListAsync(cancellationToken);

        return events
            .Select(e => (e.UserId, e.OccurredAt, Roles: JsonSerializer.Deserialize<RolePayload>(e.Payload!, JsonSerializerOptions.Web)))
            .Where(e => e.Roles is { From: not null, To: not null })
            .Select(e => new RoleChangeRow(e.UserId, e.OccurredAt, e.Roles!.From!, e.Roles.To!))
            .ToList();
    }

    private sealed record RolePayload(string? From, string? To);

    public async Task<IReadOnlyList<WorkEntryTime>> WorkAsync(
        IReadOnlyCollection<long> userIds,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
        => await db.WorkEntries
            .AsNoTracking()
            .Where(e => userIds.Contains(e.UserId) && e.DeletedAt == null && e.StartedAt >= fromUtc && e.StartedAt < toUtc)
            .Select(e => new WorkEntryTime(e.Id, e.UserId, e.ProjectId, e.StartedAt, e.DurationMinutes))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkEntryTime>> WorkOnProjectsAsync(
        IReadOnlyCollection<long> projectIds,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
        => await db.WorkEntries
            .AsNoTracking()
            .Where(e => projectIds.Contains(e.ProjectId) && e.DeletedAt == null && e.StartedAt >= fromUtc && e.StartedAt < toUtc)
            .Select(e => new WorkEntryTime(e.Id, e.UserId, e.ProjectId, e.StartedAt, e.DurationMinutes))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<User>> UsersAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
        => await db.Users.AsNoTracking()
            .Include(u => u.Team)
            .Include(u => u.EmploymentPeriods)
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProjectLabel>> ProjectsAsync(string domain, CancellationToken cancellationToken = default)
        => await db.Projects
            .AsNoTracking()
            .Where(p => p.DomainCode == domain)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectLabel(p.Id, p.Title, p.Client != null ? p.Client.Name : null, p.Client != null ? p.Client.Organisation : null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<long, ProjectLook>> ProjectLooksAsync(
        IReadOnlyCollection<long> projectIds,
        CancellationToken cancellationToken = default)
        => await db.Projects
            .AsNoTracking()
            .Where(p => projectIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new ProjectLook(p.Icon, p.Tint), cancellationToken);

    public async Task<IReadOnlyList<HourExcuse>> ExcusesAsync(IReadOnlyCollection<long> userIds, DateOnly month, CancellationToken cancellationToken = default)
        => await db.HourExcuses
            .AsNoTracking()
            .Include(x => x.Days)
            .Where(x => userIds.Contains(x.UserId) && x.MonthStart == month && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
}
