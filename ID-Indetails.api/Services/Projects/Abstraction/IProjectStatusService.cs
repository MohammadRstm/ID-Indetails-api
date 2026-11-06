using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Services.Projects.Abstraction;

public interface IProjectStatusService
{
    /// <summary>Applies one status change, finalising or reopening the project when it calls for it.</summary>
    /// <param name="keepPaidAhead">Completing a client project paid beyond its price raises the price to what was received.</param>
    /// <param name="settleOwed">
    /// Completing a client project whose client still owes: what becomes of all of it, which lowers the price to what was
    /// received. Null leaves it owed.
    /// </param>
    Task<ProjectResponse> ChangeAsync(
        long projectId,
        ProjectStatusRules.Action action,
        CancellationToken cancellationToken = default,
        bool keepPaidAhead = false,
        OwedSettlement? settleOwed = null);
}
