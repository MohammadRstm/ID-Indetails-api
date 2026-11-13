using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Reports;
using ID_Indetails.api.DTO.Response.Reports;
using ID_Indetails.api.Services.Reports.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The Design reports and dashboard. Callers who can't see Design get 404; money only with financial:view.</summary>
[ApiController]
[Route("api/reports")]
public sealed class ReportsController(IReportService reportService, IUtilizationService utilization) : ControllerBase
{
    /// <summary>Design employees' logged against expected hours in a month, with excuses, overtime and project mix. Hours only.</summary>
    [HttpGet("utilization")]
    [HasPermission(PermissionCodes.ReportView)]
    public Task<UtilizationResponse> Utilization([FromQuery] UtilizationQuery query, CancellationToken cancellationToken)
        => utilization.MonthAsync(query, cancellationToken);

    [HttpGet("work")]
    [HasPermission(PermissionCodes.ReportView)]
    public Task<WorkReportResponse> Work([FromQuery] WorkReportQuery query, CancellationToken cancellationToken)
        => reportService.WorkReportAsync(query, cancellationToken);

    [HttpGet("overview")]
    [HasPermission(PermissionCodes.ReportView)]
    public Task<OverviewResponse> Overview([FromQuery] OverviewQuery query, CancellationToken cancellationToken)
        => reportService.OverviewAsync(query, cancellationToken);
}
