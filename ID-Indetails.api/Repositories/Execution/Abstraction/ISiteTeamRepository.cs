using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Execution.Abstraction;

/// <summary>Someone overseeing a site now, in the order they were put on it.</summary>
public sealed record SiteStaffRow(long ProjectId, long UserId, string Name);

/// <summary>A site its staff member oversees, as their list of sites shows it.</summary>
public sealed record StaffSiteRow(long Id, string Title, string? ClientName, ProjectStatus Status, string? Location, DateTime AssignedAt);

/// <summary>A daily log with what the API shows about it.</summary>
public sealed record SiteLogRow(SiteLog Log, string ProjectTitle, string UserName);

/// <summary>
/// Daily logs of the given sites, people and company days (UTC bounds: <paramref name="FromUtc"/> inclusive,
/// <paramref name="ToUtc"/> exclusive), newest first. <paramref name="Text"/> matches the log or the site's title.
/// </summary>
public sealed record SiteLogSearch(
    IReadOnlyCollection<string> Domains,
    long? UserId = null,
    long? ProjectId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Text = null,
    int Page = 1,
    int PageSize = 25);

/// <summary>Who oversees each Execution site, and what they note there each day.</summary>
public interface ISiteTeamRepository
{
    /// <summary>The people overseeing these sites now. Not tracked.</summary>
    Task<IReadOnlyList<SiteStaffRow>> StaffAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Every row of the site, those taken off included. Tracked, to change who oversees it.</summary>
    Task<List<ProjectStaff>> StaffForUpdateAsync(long projectId, CancellationToken cancellationToken = default);

    Task<bool> IsOnTeamAsync(long projectId, long userId, CancellationToken cancellationToken = default);

    /// <summary>The people with these ids, to check they may oversee a site.</summary>
    Task<IReadOnlyList<SiteTeamRules.Candidate>> CandidatesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    /// <summary>The sites the person oversees now, newest assignment first.</summary>
    Task<IReadOnlyList<StaffSiteRow>> SitesOfAsync(long userId, CancellationToken cancellationToken = default);

    void AddStaff(ProjectStaff staff);

    Task<(IReadOnlyList<SiteLogRow> Items, int TotalCount)> SearchLogsAsync(SiteLogSearch search, CancellationToken cancellationToken = default);

    Task<SiteLogRow?> GetLogAsync(long id, CancellationToken cancellationToken = default);

    Task<SiteLog?> GetLogForUpdateAsync(long id, CancellationToken cancellationToken = default);

    void AddLog(SiteLog log);
}
