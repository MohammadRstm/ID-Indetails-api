using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Meetings.Abstraction;

public sealed record MeetingView(
    long Id,
    long ProjectId,
    string ProjectTitle,
    string ProjectDomain,
    DateTime StartsAt,
    DateTime EndsAt,
    string? Topic,
    string? Description,
    long? CreatedById,
    string? CreatedByName,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? ProjectIcon,
    string? ProjectTint);

/// <summary>
/// Live meetings of projects in <paramref name="Domains"/>. Times are UTC: a meeting matches when it overlaps
/// [From, To).
/// </summary>
public sealed record MeetingSearch(
    IReadOnlyList<string> Domains,
    long? ProjectId = null,
    long? CreatedById = null,
    DateTime? From = null,
    DateTime? To = null);

public interface IMeetingRepository
{
    /// <summary>Oldest first. Not tracked.</summary>
    Task<IReadOnlyList<MeetingView>> ListAsync(MeetingSearch search, CancellationToken cancellationToken = default);

    /// <summary>A live meeting. Not tracked.</summary>
    Task<MeetingView?> GetViewAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>A live meeting, tracked, with its project.</summary>
    Task<ProjectMeeting?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    void Add(ProjectMeeting meeting);
}
