using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Dashboard;
using ID_Indetails.api.Services.Dashboard.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>A domain dashboard's key figures, projects by status and deadlines. Its money cards come from /api/finance.</summary>
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet("{domain}")]
    [HasPermission(PermissionCodes.ReportView)]
    public Task<DashboardResponse> Get(string domain, CancellationToken cancellationToken)
        => dashboardService.GetAsync(domain, cancellationToken);

    /// <summary>Open requirements due in the next 14 days, overdue ones included, with the overdue count.</summary>
    [HttpGet("{domain}/deadlines")]
    [HasPermission(PermissionCodes.ReportView)]
    public Task<UpcomingDeadlinesResponse> Deadlines(string domain, CancellationToken cancellationToken)
        => dashboardService.DeadlinesAsync(domain, cancellationToken);
}
