using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Services.Execution.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>The daily logs Execution site staff write on the sites they oversee, and their list of sites.</summary>
[ApiController]
[Route("api")]
public sealed class SiteLogsController(ISiteLogService siteLogs) : ControllerBase
{
    [HttpPost("projects/{projectId:long}/site-logs")]
    [HasPermission(PermissionCodes.SiteLogCreate)]
    public async Task<ActionResult<SiteLogResponse>> Create(long projectId, SaveSiteLogRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await siteLogs.CreateAsync(projectId, request, cancellationToken));

    /// <summary>The writer changes a log on the day it was written.</summary>
    [HttpPut("site-logs/{id:long}")]
    [HasPermission(PermissionCodes.SiteLogCreate)]
    public Task<SiteLogResponse> Update(long id, SaveSiteLogRequest request, CancellationToken cancellationToken)
        => siteLogs.UpdateAsync(id, request, cancellationToken);

    [HttpGet("me/site-logs")]
    [HasPermission(PermissionCodes.SiteLogCreate)]
    public Task<PagedResponse<SiteLogResponse>> Mine([FromQuery] SiteLogListQuery query, CancellationToken cancellationToken)
        => siteLogs.MineAsync(query, cancellationToken);

    [HttpGet("site-logs")]
    [HasPermission(PermissionCodes.SiteLogViewAll)]
    public Task<PagedResponse<SiteLogResponse>> List([FromQuery] SiteLogListQuery query, CancellationToken cancellationToken)
        => siteLogs.ListAsync(query, cancellationToken);

    /// <summary>The sites the caller oversees, newest first.</summary>
    [HttpGet("me/sites")]
    [HasAnyPermission(PermissionCodes.SiteLogCreate, PermissionCodes.LabourReportCreate)]
    public Task<IReadOnlyList<MySiteResponse>> MySites(CancellationToken cancellationToken)
        => siteLogs.MySitesAsync(cancellationToken);
}
