using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Portal.Abstraction;

public interface IPortalAccessRepository
{
    Task<PortalProjectFacts?> GetProjectAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Tracked, live or not.</summary>
    Task<PortalProjectAccess?> GetAsync(long accountId, long projectId, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with the account loaded.</summary>
    Task<PortalProjectAccess?> GetByIdAsync(long accessId, CancellationToken cancellationToken = default);

    /// <summary>Every access row of the project, ended ones included, untracked with the account loaded.</summary>
    Task<IReadOnlyList<PortalProjectAccess>> ForProjectAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Tracked: the project's live access rows.</summary>
    Task<IReadOnlyList<PortalProjectAccess>> LiveForProjectAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Whether the account has access to any project other than <paramref name="exceptProjectId"/>.</summary>
    Task<bool> HasLiveAccessAsync(long accountId, long exceptProjectId, CancellationToken cancellationToken = default);

    /// <summary>The Design projects the account may open now, by title.</summary>
    Task<IReadOnlyList<PortalProjectSummary>> ProjectsOfAsync(long accountId, CancellationToken cancellationToken = default);

    void Add(PortalProjectAccess access);
}
