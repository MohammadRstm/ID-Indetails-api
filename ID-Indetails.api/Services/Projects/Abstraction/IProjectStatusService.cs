using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.DTO.Response.Projects;

namespace ID_Indetails.api.Services.Projects.Abstraction;

public interface IProjectStatusService
{
    /// <summary>Applies one status change, finalising or reopening the project when it calls for it.</summary>
    /// <param name="keepPaidAhead">Completing a client project paid beyond its price raises the price to what was received.</param>
    Task<ProjectResponse> ChangeAsync(long projectId, ProjectStatusRules.Action action, CancellationToken cancellationToken = default, bool keepPaidAhead = false);
}
