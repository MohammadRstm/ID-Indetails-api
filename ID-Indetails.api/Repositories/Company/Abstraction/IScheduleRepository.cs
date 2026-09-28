using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Company.Abstraction;

public interface IScheduleRepository
{
    /// <summary>People who aren't deactivated, in the given domains (super admins only when asked), by name.</summary>
    Task<IReadOnlyList<User>> PeopleAsync(
        IReadOnlyList<string> domains,
        bool includeSuperAdmins,
        string? domain,
        string? search,
        CancellationToken cancellationToken = default);

    /// <summary>Every schedule of these people with its days, not tracked.</summary>
    Task<IReadOnlyList<UserSchedule>> SchedulesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with its days.</summary>
    Task<UserSchedule?> GetForUpdateAsync(long userId, DateOnly effectiveFrom, CancellationToken cancellationToken = default);

    void Add(UserSchedule schedule);

    void RemoveDays(IEnumerable<UserScheduleDay> days);
}
