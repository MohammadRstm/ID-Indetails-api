using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Labour;
using ID_Indetails.api.DTO.Response.Labour;
using ID_Indetails.api.Services.Labour.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Ground labour of an Execution site. Projects of a domain the caller can't see read as 404.</summary>
[ApiController]
[Route("api/projects/{projectId:long}")]
public sealed class LabourController(ILabourService labourService) : ControllerBase
{
    [HttpGet("labour-weeks")]
    [HasPermission(PermissionCodes.ProjectView)]
    public Task<LabourWeekListResponse> ListWeeks(long projectId, CancellationToken cancellationToken)
        => labourService.ListWeeksAsync(projectId, cancellationToken);

    /// <summary>A week not yet reported comes back filled in from the defaults, up to today.</summary>
    [HttpGet("labour-weeks/{weekStart}")]
    [HasPermission(PermissionCodes.ProjectView)]
    public Task<LabourWeekResponse> GetWeek(long projectId, DateOnly weekStart, CancellationToken cancellationToken)
        => labourService.GetWeekAsync(projectId, weekStart, cancellationToken);

    /// <summary>New weeks and your own with <c>labour_report:create</c>, anyone's with <c>labour_report:manage</c>.</summary>
    [HttpPut("labour-weeks/{weekStart}")]
    [HasAnyPermission(PermissionCodes.LabourReportCreate, PermissionCodes.LabourReportManage)]
    public Task<LabourWeekResponse> SubmitWeek(long projectId, DateOnly weekStart, SubmitLabourWeekRequest request, CancellationToken cancellationToken)
        => labourService.SubmitWeekAsync(projectId, weekStart, request, cancellationToken);

    [HttpGet("labour-defaults")]
    [HasPermission(PermissionCodes.ProjectView)]
    public Task<LabourDefaultsResponse> GetDefaults(long projectId, CancellationToken cancellationToken)
        => labourService.GetDefaultsAsync(projectId, cancellationToken);

    /// <summary>Admins only: applies from today, and a second change the same day replaces the first.</summary>
    [HttpPost("labour-defaults")]
    [HasPermission(PermissionCodes.LabourReportManage)]
    public Task<LabourDefaultsResponse> SetDefaults(long projectId, SetLabourDefaultsRequest request, CancellationToken cancellationToken)
        => labourService.SetDefaultsAsync(projectId, request, cancellationToken);
}
