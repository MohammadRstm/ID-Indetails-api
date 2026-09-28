using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.WorkEntries.Abstraction;

/// <summary>A work entry with what the API shows about it.</summary>
public sealed record WorkEntryView(
    long Id,
    long UserId,
    string UserName,
    long ProjectId,
    string ProjectTitle,
    ProjectStatus ProjectStatus,
    string ProjectDomain,
    long? ClientId,
    string? ClientName,
    string Description,
    DateTime StartedAt,
    DateTime EndedAt,
    int Minutes,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int CommentCount);

/// <summary>
/// Live entries on Design projects. Times are UTC; <paramref name="From"/> is inclusive and <paramref name="To"/>
/// exclusive, both on the start time. <paramref name="Text"/> matches the description or project title.
/// </summary>
public sealed record WorkEntrySearch(
    long? UserId = null,
    long? ProjectId = null,
    long? ClientId = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Text = null,
    int Page = 1,
    int PageSize = 25);

/// <summary>The few fields totals and charts are calculated from.</summary>
public sealed record WorkRow(long Id, long UserId, long ProjectId, DateTime StartedAt, DateTime EndedAt, int Minutes, DateTime CreatedAt);

public sealed record WorkPerson(long Id, string Name, string Email, string? TeamName, string? DomainCode);

/// <summary>A project as the Design work pages list it.</summary>
public sealed record WorkProject(
    long Id,
    string Title,
    string? Description,
    string? ClientName,
    ProjectStatus Status,
    int ProgressPct,
    DateOnly StartDate,
    DateTime CreatedAt);

/// <summary>One person's live work on one project.</summary>
public sealed record Contribution(long ProjectId, long UserId, string UserName, int Minutes, int Entries, DateTime LastStartedAt);

public interface IWorkEntryRepository
{
    /// <summary>
    /// Locks the person's user row until the transaction ends, so two changes to their entries are
    /// checked for overlaps one after the other.
    /// </summary>
    Task LockPersonAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with its project. Live entries only.</summary>
    Task<WorkEntry?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with its project. Deleted entries only.</summary>
    Task<WorkEntry?> GetDeletedForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with their projects. Live entries of <paramref name="userId"/> only.</summary>
    Task<IReadOnlyList<WorkEntry>> ListOwnForUpdateAsync(long userId, IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);

    /// <summary>The person's live entries overlapping [from, to). Not tracked.</summary>
    Task<IReadOnlyList<WorkEntryRules.Span>> SpansAsync(long userId, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>A live entry. Not tracked.</summary>
    Task<WorkEntryView?> GetViewAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Newest first (by start). Not tracked.</summary>
    Task<(IReadOnlyList<WorkEntryView> Items, int TotalCount)> SearchAsync(WorkEntrySearch search, CancellationToken cancellationToken = default);

    /// <summary>Every matching entry, oldest first, ignoring the search's paging. Not tracked.</summary>
    Task<IReadOnlyList<WorkEntryView>> ListAsync(WorkEntrySearch search, CancellationToken cancellationToken = default);

    /// <summary>Every matching entry's totals fields, ignoring the search's text and paging. Not tracked.</summary>
    Task<IReadOnlyList<WorkRow>> RowsAsync(WorkEntrySearch search, CancellationToken cancellationToken = default);

    /// <summary>Active and on-hold Design projects, newest first. Not tracked.</summary>
    Task<IReadOnlyList<WorkProject>> OpenProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>Each person's minutes, entries and latest start on each of the projects. Not tracked.</summary>
    Task<IReadOnlyList<Contribution>> ContributionsAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Names, emails, teams and domains of the given people. Not tracked.</summary>
    Task<IReadOnlyList<WorkPerson>> PeopleAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Active people who might hold <paramref name="permissionCode"/> in the domain: its members, super admins,
    /// and anyone granted it personally. The caller still checks each one's effective permissions.
    /// </summary>
    Task<IReadOnlyList<long>> PeopleWhoMayHoldAsync(string domainCode, string permissionCode, CancellationToken cancellationToken = default);

    /// <summary>Locks the domain row until the transaction ends, to serialise updates of shared digest notifications.</summary>
    Task LockDomainAsync(string domainCode, CancellationToken cancellationToken = default);

    void Add(WorkEntry entry);
}
