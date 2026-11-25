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
public sealed class ReportsController(IReportService reportService, IUtilizationService utilization, INotLoggingService notLogging) : ControllerBase
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

    /// <summary>The Design employees who have logged no work this week (the dashboard's "not logging" item). Hours only with work_entry:view_all.</summary>
    [HttpGet("not-logging")]
    [HasPermission(PermissionCodes.ReportView)]
    public Task<NotLoggingResponse> NotLogging(CancellationToken cancellationToken)
        => notLogging.ListAsync(cancellationToken);

    /// <summary>Asks one of them to log their hours. One reminder a day per person, from anyone.</summary>
    [HttpPost("not-logging/{userId:long}/reminder")]
    [HasPermission(PermissionCodes.ReportView)]
    public Task<HoursRemindersResponse> Remind(long userId, CancellationToken cancellationToken)
        => notLogging.RemindAsync(userId, cancellationToken);

    /// <summary>Asks everyone on the list who wasn't reminded today to log their hours.</summary>
    [HttpPost("not-logging/reminders")]
    [HasPermission(PermissionCodes.ReportView)]
    public Task<HoursRemindersResponse> RemindAll(CancellationToken cancellationToken)
        => notLogging.RemindAllAsync(cancellationToken);
}
