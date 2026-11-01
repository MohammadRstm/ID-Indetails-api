using ID_Indetails.api.Business.Meetings;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Meetings.Abstraction;

public sealed record MeetingPerson(long Id, string Name);

/// <param name="ProjectId">Null for a general meeting.</param>
/// <param name="Domain">The project's domain, or a general meeting's own; null for a company-wide meeting.</param>
public sealed record MeetingView(
    long Id,
    long? ProjectId,
    string? ProjectTitle,
    string? Domain,
    DateTime StartsAt,
    DateTime EndsAt,
    string? Topic,
    string? Description,
    long? CreatedById,
    string? CreatedByName,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? ProjectIcon,
    string? ProjectTint,
    IReadOnlyList<MeetingPerson> Attendees);

/// <summary>
/// Live meetings of projects in <paramref name="Domains"/>, and the general meetings <paramref name="Viewer"/> may see
/// there or company-wide (<see cref="MeetingRules.CanSeeGeneral"/>). Times are UTC: a meeting matches when it overlaps
/// [From, To). With <paramref name="ProjectId"/>, that project's meetings only.
/// </summary>
public sealed record MeetingSearch(
    IReadOnlyList<string> Domains,
    MeetingRules.Viewer Viewer,
    long? ProjectId = null,
    long? CreatedById = null,
    DateTime? From = null,
    DateTime? To = null);

/// <param name="DomainCode">Null for a super admin.</param>
public sealed record MeetingInvitee(long Id, string? DomainCode);

public interface IMeetingRepository
{
    /// <summary>Oldest first. Not tracked.</summary>
    Task<IReadOnlyList<MeetingView>> ListAsync(MeetingSearch search, CancellationToken cancellationToken = default);

    /// <summary>A live meeting. Not tracked.</summary>
    Task<MeetingView?> GetViewAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>A live meeting, tracked, with its project and attendees.</summary>
    Task<ProjectMeeting?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Those of the given people who are active, for choosing attendees.</summary>
    Task<IReadOnlyList<MeetingInvitee>> ActivePeopleAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);

    void Add(ProjectMeeting meeting);
}
