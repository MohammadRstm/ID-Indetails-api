using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Portal.Abstraction;

/// <summary>Staff comments on a project's files.</summary>
public interface IProjectFileCommentRepository
{
    /// <summary>The item's comments, oldest first.</summary>
    Task<IReadOnlyList<ProjectFileCommentView>> ListAsync(long itemId, CancellationToken cancellationToken = default);

    Task<ProjectFileCommentView?> ViewAsync(long commentId, CancellationToken cancellationToken = default);

    /// <summary>The comment, when it belongs to the project. Tracked.</summary>
    Task<ProjectFileComment?> GetAsync(long projectId, long commentId, CancellationToken cancellationToken = default);

    /// <summary>Everyone who has commented on the item, each once.</summary>
    Task<IReadOnlyList<long>> AuthorsAsync(long itemId, CancellationToken cancellationToken = default);

    /// <summary>Each commented item of the project: how many comments it has, and how many aren't fixed.</summary>
    Task<IReadOnlyDictionary<long, ProjectFileCommentCounts>> CountsAsync(long projectId, CancellationToken cancellationToken = default);

    void Add(ProjectFileComment comment);

    void Remove(ProjectFileComment comment);
}
