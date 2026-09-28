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
public sealed class ReportsController(IReportService reportService) : ControllerBase
{
    [HttpGet("work")]
    [HasPermission(PermissionCodes.ReportView)]
    public Task<WorkReportResponse> Work([FromQuery] WorkReportQuery query, CancellationToken cancellationToken)
        => reportService.WorkReportAsync(query, cancellationToken);

    [HttpGet("overview")]
    [HasPermission(PermissionCodes.ReportView)]
    public Task<OverviewResponse> Overview([FromQuery] OverviewQuery query, CancellationToken cancellationToken)
        => reportService.OverviewAsync(query, cancellationToken);
}
