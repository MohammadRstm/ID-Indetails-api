using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Employees.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Employees.Implementation;

public sealed class EmployeeRepository(IdIndetailsDbContext db) : IEmployeeRepository
{
    public async Task<(IReadOnlyList<EmployeeRow> Items, int TotalCount)> SearchAsync(
        EmployeeSearch search,
        CancellationToken cancellationToken = default)
    {
        var domains = search.Domains.ToList();
        var query = db.Users.AsNoTracking().Where(u =>
            u.DomainCode != null ? domains.Contains(u.DomainCode) : search.IncludeSuperAdmins);

        query = search.Status is { } status
            ? query.Where(u => u.Status == status)
            : query.Where(u => u.Status != UserStatus.Deactivated);

        if (search.TeamId is { } teamId)
        {
            query = query.Where(u => u.TeamId == teamId);
        }

        if (search.Role is { } role)
        {
            query = query.Where(u => u.RoleCode == role);
        }

        if (search.Domain is { } domain)
        {
            query = query.Where(u => u.DomainCode == domain);
        }

        // Every word must appear in the name, email or position (the collation ignores case).
        var words = (search.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            query = query.Where(u =>
                (u.FirstName != null && u.FirstName.Contains(word))
                || (u.LastName != null && u.LastName.Contains(word))
                || u.Email.Contains(word)
                || (u.Position != null && u.Position.Contains(word)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await Project(query
                .OrderBy(u => u.FirstName == null)
                .ThenBy(u => u.FirstName)
                .ThenBy(u => u.LastName)
                .ThenBy(u => u.Email)
                .Skip((search.Page - 1) * search.PageSize)
                .Take(search.PageSize))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<EmployeeRow?> GetAsync(long userId, CancellationToken cancellationToken = default)
        => Project(db.Users.AsNoTracking().Where(u => u.Id == userId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<UserPayHistory>> GetPayHistoryAsync(long userId, CancellationToken cancellationToken = default)
        => await db.UserPayHistory
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.EffectiveFrom)
            .ToListAsync(cancellationToken);

    public Task<UserPayHistory?> GetPayInForceAsync(long userId, DateOnly day, CancellationToken cancellationToken = default)
        => db.UserPayHistory
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.EffectiveFrom <= day)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<DateOnly?> GetLatestPayDateAsync(long userId, CancellationToken cancellationToken = default)
        => db.UserPayHistory
            .Where(p => p.UserId == userId)
            .MaxAsync(p => (DateOnly?)p.EffectiveFrom, cancellationToken);

    public async Task<int> LockActiveSuperAdminsAsync(CancellationToken cancellationToken = default)
        => (await db.Database
            .SqlQuery<long>($"SELECT `id` AS `Value` FROM `users` WHERE `role_code` = {RoleCodes.SuperAdmin} AND `status` = 'active' FOR UPDATE")
            .ToListAsync(cancellationToken))
            .Count;

    private IQueryable<EmployeeRow> Project(IQueryable<User> users)
        => users.Select(u => new EmployeeRow(
            u.Id,
            u.Email,
            u.FirstName,
            u.LastName,
            u.Phone,
            u.RoleCode,
            u.DomainCode,
            u.TeamId,
            u.Team != null ? u.Team.Name : null,
            u.Position,
            u.Status,
            db.UserPermissionOverrides.Any(o => o.UserId == u.Id),
            u.JoinedOn,
            u.LastSeenAt,
            u.CreatedAt));
}
