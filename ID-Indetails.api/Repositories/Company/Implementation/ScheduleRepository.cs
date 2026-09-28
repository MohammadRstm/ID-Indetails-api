using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Company.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Company.Implementation;

public sealed class ScheduleRepository(IdIndetailsDbContext db) : IScheduleRepository
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
            .Where(u => u.Status != UserStatus.Deactivated
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
                || (u.Position != null && u.Position.Contains(word))
                || (u.Team != null && u.Team.Name.Contains(word)));
        }

        return await query
            .OrderBy(u => u.DomainCode)
            .ThenBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserSchedule>> SchedulesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
        => await db.UserSchedules
            .AsNoTracking()
            .Include(s => s.Days)
            .Where(s => userIds.Contains(s.UserId))
            .ToListAsync(cancellationToken);

    public Task<UserSchedule?> GetForUpdateAsync(long userId, DateOnly effectiveFrom, CancellationToken cancellationToken = default)
        => db.UserSchedules
            .Include(s => s.Days)
            .SingleOrDefaultAsync(s => s.UserId == userId && s.EffectiveFrom == effectiveFrom, cancellationToken);

    public void Add(UserSchedule schedule) => db.UserSchedules.Add(schedule);

    public void RemoveDays(IEnumerable<UserScheduleDay> days) => db.UserScheduleDays.RemoveRange(days);
}
