using ID_Indetails.api.Business.Requirements;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Requirements.Abstraction;

public interface IRequirementRepository
{
    /// <summary>
    /// Live requirements of the project, not tracked, with assignees and the person who ticked them.
    /// Open ones first (oldest first), then done ones (most recently done first).
    /// </summary>
    Task<IReadOnlyList<ProjectRequirement>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>A live requirement of the project, tracked, with its assignees.</summary>
    Task<ProjectRequirement?> GetForUpdateAsync(long projectId, long requirementId, CancellationToken cancellationToken = default);

    /// <summary>Who these people are, for checking assignees. Unknown ids are left out.</summary>
    Task<IReadOnlyList<RequirementRules.Person>> GetPeopleAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    /// <summary>Display names (full name, or email before signup) by user id.</summary>
    Task<IReadOnlyDictionary<long, string>> GetNamesAsync(IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);

    /// <summary>How many live (not deleted) requirements the project has, and how many are done.</summary>
    Task<(int Total, int Done)> CountAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Ticks every open requirement as done automatically (when the project is finalised). Runs immediately.</summary>
    Task AutoTickOpenAsync(long projectId, DateTime doneAt, long doneById, CancellationToken cancellationToken = default);

    /// <summary>Reopens exactly the requirements that were ticked automatically. Runs immediately.</summary>
    Task UntickAutoDoneAsync(long projectId, CancellationToken cancellationToken = default);

    void Add(ProjectRequirement requirement);
}
