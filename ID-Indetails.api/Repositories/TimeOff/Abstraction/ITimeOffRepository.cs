using ID_Indetails.api.Business.TimeOff;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.TimeOff.Abstraction;

/// <summary>A request with what the API shows about it: who asked, their department, and who decided or ended it.</summary>
public sealed record TimeOffRow(
    TimeOffRequest Request,
    string PersonName,
    long? PhotoFileId,
    long? TeamId,
    string? TeamName,
    string? DecidedByName,
    string? EndedByName);

public interface ITimeOffRepository
{
    void Add(TimeOffRequest request);

    /// <summary>Tracked, and locked (<c>SELECT … FOR UPDATE</c>) until the current transaction ends.</summary>
    Task<TimeOffRequest?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    Task<TimeOffRow?> GetRowAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Someone's requests, newest first. Not tracked.</summary>
    Task<IReadOnlyList<TimeOffRow>> OfUserAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A view of everyone's requests, with <paramref name="today"/> splitting current from past. Pending and current
    /// ones by first day; past ones newest first, a page at a time.
    /// </summary>
    Task<(IReadOnlyList<TimeOffRow> Items, int Total)> ListAsync(TimeOffView view, DateOnly today, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Whether someone has a pending or approved request on any day from <paramref name="from"/> to <paramref name="to"/>.</summary>
    Task<bool> HeldOnAsync(long userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// The granted requests (approved, rejected, ended) of these people that may take a day from <paramref name="from"/>
    /// to <paramref name="to"/>; <see cref="TimeOffRules.Period.LastTaken"/> says which days they did take.
    /// </summary>
    Task<IReadOnlyList<(long UserId, TimeOffRules.Period Period)>> GrantedAsync(
        IReadOnlyCollection<long>? userIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Someone's pending and approved requests with days from <paramref name="from"/> on. Tracked.</summary>
    Task<IReadOnlyList<TimeOffRequest>> OpenFromAsync(long userId, DateOnly from, CancellationToken cancellationToken = default);

    /// <summary>Pending requests whose first day is on or before <paramref name="day"/>, with who asked. Tracked.</summary>
    Task<IReadOnlyList<TimeOffRequest>> PendingStartingByAsync(DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>Approved requests under way on <paramref name="day"/>, with who asked. Not tracked.</summary>
    Task<IReadOnlyList<TimeOffRequest>> UnderWayAsync(DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>The meetings someone is in that overlap these UTC bounds, earliest first.</summary>
    Task<IReadOnlyList<ProjectMeeting>> MeetingsOfAsync(long userId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>The active people of a department, whatever their role.</summary>
    Task<IReadOnlyList<long>> ActiveTeamMembersAsync(long teamId, CancellationToken cancellationToken = default);
}
