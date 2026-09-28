using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Projects.Abstraction;

/// <summary>Keeps the cached <c>projects.progress_pct</c> in step with the project's requirements.</summary>
public interface IProjectProgress
{
    /// <summary>
    /// Recalculates the progress of a tracked project from its saved requirements and stages a
    /// <c>progress_set</c> event when it changed. Call inside the transaction that changed them.
    /// </summary>
    Task RefreshAsync(Project project, long actorId, CancellationToken cancellationToken = default);
}
