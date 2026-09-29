using ID_Indetails.api.DTO.Response.Dashboard;

namespace ID_Indetails.api.Services.Dashboard.Abstraction;

public interface IDashboardService
{
    Task<DashboardResponse> GetAsync(string domain, CancellationToken cancellationToken = default);

    Task<UpcomingDeadlinesResponse> DeadlinesAsync(string domain, CancellationToken cancellationToken = default);
}
