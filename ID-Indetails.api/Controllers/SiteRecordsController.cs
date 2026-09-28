using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Services.Execution.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>What site staff recorded on Execution sites: project cards and each person's records.</summary>
[ApiController]
[Route("api")]
public sealed class SiteRecordsController(ISiteRecordsService siteRecords) : ControllerBase
{
    /// <summary>Cards for the given sites. Projects the caller can't see, and Design projects, are left out.</summary>
    [HttpGet("projects/site-summary")]
    [HasPermission(PermissionCodes.ProjectView)]
    public Task<IReadOnlyList<SiteSummaryResponse>> Summaries([FromQuery] SiteSummaryQuery query, CancellationToken cancellationToken)
        => siteRecords.SummariesAsync(query, cancellationToken);

    /// <summary>The materials and labour weeks the caller recorded.</summary>
    [HttpGet("me/site-records")]
    [HasAnyPermission(PermissionCodes.MaterialCreate, PermissionCodes.MaterialManage, PermissionCodes.LabourReportCreate, PermissionCodes.LabourReportManage)]
    public Task<SiteRecordsResponse> Mine(CancellationToken cancellationToken)
        => siteRecords.MineAsync(cancellationToken);

    /// <summary>Someone's records, on sites the caller can see. People of a domain the caller can't see read as 404.</summary>
    [HttpGet("employees/{userId:long}/site-records")]
    [HasPermission(PermissionCodes.EmployeeView)]
    public Task<SiteRecordsResponse> Of(long userId, CancellationToken cancellationToken)
        => siteRecords.OfAsync(userId, cancellationToken);
}
