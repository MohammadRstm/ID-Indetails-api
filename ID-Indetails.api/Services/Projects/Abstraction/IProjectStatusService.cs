using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Services.Projects.Abstraction;

public interface IProjectStatusService
{
    /// <summary>Applies one status change, finalising or reopening the project when it calls for it.</summary>
    Task<ProjectResponse> ChangeAsync(long projectId, ProjectStatusRules.Action action, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same, saying what happens to an Execution project's money in the box: <c>remove</c> or <c>keep</c> when it
    /// closes, <c>readd</c> or <c>fresh</c> when a removed one reopens. With none, a closing project waits for a decision
    /// on the Box page and a reopening one counts afresh.
    /// </summary>
    Task<ProjectResponse> ChangeAsync(long projectId, ProjectStatusRules.Action action, BoxChoice? box, CancellationToken cancellationToken = default);
}
