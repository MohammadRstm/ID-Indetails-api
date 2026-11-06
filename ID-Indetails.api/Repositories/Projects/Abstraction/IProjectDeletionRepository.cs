using ID_Indetails.api.Business.Projects;

namespace ID_Indetails.api.Repositories.Projects.Abstraction;

/// <summary>What a project holds, and deleting it with all of it (<see cref="ProjectDeletionRules"/>).</summary>
public interface IProjectDeletionRepository
{
    /// <summary>What keeps the project from being deleted, counted. <paramref name="openFromUtc"/> is when the open month began.</summary>
    Task<ProjectDeletionRules.Blocking> BlockingAsync(long projectId, DateTime openFromUtc, CancellationToken cancellationToken = default);

    /// <summary>What deleting it removes, in words ("3 site expenses"): only what it holds any of.</summary>
    Task<IReadOnlyList<string>> RemovesAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the project and everything on it, what points at a row before the row itself, and unlinks the website page
    /// that showed it. Returns the keys of its client portal's stored files, to delete from the content store once the
    /// transaction is committed.
    /// </summary>
    Task<IReadOnlyList<string>> DeleteAsync(long projectId, CancellationToken cancellationToken = default);
}
