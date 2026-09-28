using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Projects.Abstraction;

public sealed record ProjectSearch(
    IReadOnlyList<string> Domains,
    string? Text,
    ProjectStatus? Status,
    long? ClientId,
    BillingType? BillingType,
    string? Domain,
    int Page,
    int PageSize);

public interface IProjectRepository
{
    /// <summary>Not tracked, with client and owner.</summary>
    Task<(IReadOnlyList<Project> Items, int TotalCount)> SearchAsync(ProjectSearch search, CancellationToken cancellationToken = default);

    /// <summary>Not tracked, with client and owner.</summary>
    Task<Project?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, and locked (<c>SELECT … FOR UPDATE</c>) until the transaction ends. Call inside a transaction.</summary>
    Task<Project?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Whether any invoice or payment (voided ones included) was made for the project.</summary>
    Task<bool> HasBillingRecordsAsync(long projectId, CancellationToken cancellationToken = default);

    void Add(Project project);
}
