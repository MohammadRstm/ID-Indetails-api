using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Projects;

namespace ID_Indetails.api.Services.Projects.Abstraction;

/// <summary>A project's cost budget, in versions, against its cost so far from the shared finance calculator.</summary>
public interface IProjectBudgetService
{
    /// <summary>The current version line by line against the cost so far, and every version. Needs <c>financial:view</c>.</summary>
    Task<ProjectBudgetResponse> GetAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the next version with its reason: <c>financial:write</c> on a Design project, <c>project:pricing_manage</c>
    /// on an Execution site, and <c>financial:view</c> to see what it returns.
    /// </summary>
    Task<ProjectBudgetResponse> SetAsync(long projectId, SetProjectBudgetRequest request, CancellationToken cancellationToken = default);
}
