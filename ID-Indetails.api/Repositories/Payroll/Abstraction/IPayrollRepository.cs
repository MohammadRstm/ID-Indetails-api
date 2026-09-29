using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Payroll.Abstraction;

/// <summary>What salaries are calculated from, loaded for everyone at once (one query per source).</summary>
public interface IPayrollRepository
{
    /// <summary>
    /// People who have joined and were active at some point since <paramref name="activeSinceUtc"/> (active now, or
    /// deactivated since then), in the given domains (super admins only when asked), with their team.
    /// </summary>
    Task<IReadOnlyList<User>> PeopleAsync(
        IReadOnlyList<string> domains,
        bool includeSuperAdmins,
        string? domain,
        string? search,
        DateTime activeSinceUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserPayHistory>> PayHistoryAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkEntryTime>> WorkAsync(
        IReadOnlyCollection<long> userIds,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Live excuses of the month, with their days.</summary>
    Task<IReadOnlyList<HourExcuse>> ExcusesAsync(IReadOnlyCollection<long> userIds, DateOnly month, CancellationToken cancellationToken = default);

    /// <summary>Work on these projects started in the window.</summary>
    Task<IReadOnlyList<WorkEntryTime>> WorkOnProjectsAsync(
        IReadOnlyCollection<long> projectIds,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>These people with their team, not tracked, whatever their status.</summary>
    Task<IReadOnlyList<User>> UsersAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    /// <summary>A domain's projects, newest first, with their client's name.</summary>
    Task<IReadOnlyList<ProjectLabel>> ProjectsAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>The icon and tint of these projects, by id.</summary>
    Task<IReadOnlyDictionary<long, ProjectLook>> ProjectLooksAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);
}

public sealed record WorkEntryTime(long Id, long UserId, long ProjectId, DateTime StartedAt, int Minutes);

public sealed record ProjectLabel(long Id, string Title, string? ClientName, string? ClientOrganisation);

public sealed record ProjectLook(string? Icon, string? Tint);
