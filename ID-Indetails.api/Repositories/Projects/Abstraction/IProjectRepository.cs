using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;

namespace ID_Indetails.api.Repositories.Projects.Abstraction;

/// <summary>Open at some point in a period: started by <paramref name="To"/> and not completed before <paramref name="FromUtc"/>.</summary>
public sealed record OpenPeriod(DateTime FromUtc, DateOnly To);

public sealed record ProjectSearch(
    IReadOnlyList<string> Domains,
    string? Text,
    ProjectStatus? Status,
    long? ClientId,
    BillingType? BillingType,
    string? Domain,
    int Page,
    int PageSize,
    long? OwnerId = null,
    OpenPeriod? OpenDuring = null,
    ProjectSort Sort = ProjectSort.Start);

public sealed record SiteRecorder(long ProjectId, long UserId, string Name, DateTime LastAt);

public interface IProjectRepository
{
    /// <summary>Not tracked, with client and owner.</summary>
    Task<(IReadOnlyList<Project> Items, int TotalCount)> SearchAsync(ProjectSearch search, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids of the domain's completed projects completed by <paramref name="beforeUtc"/> with no activity event, client
    /// portal chat message or (undeleted) comment after it.
    /// </summary>
    Task<IReadOnlyList<long>> QuietCompletedAsync(string domain, DateTime beforeUtc, CancellationToken cancellationToken = default);

    /// <summary>Live work on these projects starting from <paramref name="fromUtc"/>, for the project list's recent figures.</summary>
    Task<IReadOnlyList<WorkRow>> RecentWorkAsync(IReadOnlyCollection<long> projectIds, DateTime fromUtc, CancellationToken cancellationToken = default);

    /// <summary>Not tracked, with client and owner.</summary>
    Task<Project?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Everyone who recorded materials or labour weeks on these sites, with when they last did. Not tracked.</summary>
    Task<IReadOnlyList<SiteRecorder>> SiteRecordersAsync(IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Tracked, and locked (<c>SELECT … FOR UPDATE</c>) until the transaction ends. Call inside a transaction.</summary>
    Task<Project?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Whether any invoice or payment (voided ones included) was made for the project.</summary>
    Task<bool> HasBillingRecordsAsync(long projectId, CancellationToken cancellationToken = default);

    void Add(Project project);
}
