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
        CancellationToken cancellationToken = default)
    {
        var codes = domains.ToList();
        var query = db.Users
            .AsNoTracking()
            .Include(u => u.Team)
            .Where(u => u.Status == UserStatus.Active
                && u.JoinedOn != null
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
        => await db.Users.AsNoTracking().Include(u => u.Team).Where(u => userIds.Contains(u.Id)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProjectLabel>> ProjectsAsync(string domain, CancellationToken cancellationToken = default)
        => await db.Projects
            .AsNoTracking()
            .Where(p => p.DomainCode == domain)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProjectLabel(p.Id, p.Title, p.Client != null ? p.Client.Name : null, p.Client != null ? p.Client.Organisation : null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<HourExcuse>> ExcusesAsync(IReadOnlyCollection<long> userIds, DateOnly month, CancellationToken cancellationToken = default)
        => await db.HourExcuses
            .AsNoTracking()
            .Include(x => x.Days)
            .Where(x => userIds.Contains(x.UserId) && x.MonthStart == month && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
}
