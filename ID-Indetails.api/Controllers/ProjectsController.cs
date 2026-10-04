using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Documents.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>Projects. Projects of a domain the caller can't see read as 404.</summary>
[ApiController]
[Route("api/projects")]
public sealed class ProjectsController(
    IProjectService projectService,
    IProjectStatusService statusService,
    IDocumentService documents) : ControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.ProjectView)]
    public Task<PagedResponse<ProjectSummaryResponse>> List([FromQuery] ProjectListQuery query, CancellationToken cancellationToken)
        => projectService.ListAsync(query, cancellationToken);

    /// <summary>The money of every project the list's filters match (paging aside), for the header above it.</summary>
    [HttpGet("totals")]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<ProjectListTotalsResponse> Totals([FromQuery] ProjectListQuery query, CancellationToken cancellationToken)
        => projectService.TotalsAsync(query, cancellationToken);

    /// <summary>Money is included only for callers with financial access.</summary>
    [HttpGet("{projectId:long}")]
    [HasPermission(PermissionCodes.ProjectView)]
    public Task<ProjectResponse> Get(long projectId, CancellationToken cancellationToken)
        => projectService.GetAsync(projectId, cancellationToken);

    /// <summary>A Design project's report as a PDF: internal (everything) or the client summary (no costs, rates, profit or margin).</summary>
    [HttpGet("{projectId:long}/report")]
    [HasPermission(PermissionCodes.FinancialView)]
    [HasPermission(PermissionCodes.WorkEntryViewAll)]
    [EnableRateLimiting(RateLimitPolicies.Export)]
    public async Task<IActionResult> Report(long projectId, [FromQuery] ProjectReportQuery query, CancellationToken cancellationToken)
    {
        var pdf = await documents.ProjectReportPdfAsync(projectId, query, cancellationToken);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }

    [HttpPost]
    [HasPermission(PermissionCodes.ProjectCreate)]
    public async Task<ActionResult<ProjectResponse>> Create(CreateProjectRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await projectService.CreateAsync(request, cancellationToken));

    [HttpPut("{projectId:long}")]
    [HasPermission(PermissionCodes.ProjectEdit)]
    public Task<ProjectResponse> Update(long projectId, UpdateProjectRequest request, CancellationToken cancellationToken)
        => projectService.UpdateAsync(projectId, request, cancellationToken);

    /// <summary>Design: client work or internal, the client and the revenue.</summary>
    [HttpPut("{projectId:long}/billing")]
    [HasPermission(PermissionCodes.ProjectEdit)]
    public Task<ProjectResponse> UpdateBilling(long projectId, UpdateProjectBillingRequest request, CancellationToken cancellationToken)
        => projectService.UpdateBillingAsync(projectId, request, cancellationToken);

    /// <summary>Execution: the project fee and the default material markup.</summary>
    [HttpPut("{projectId:long}/pricing")]
    [HasPermission(PermissionCodes.ProjectPricingManage)]
    public Task<ProjectResponse> UpdatePricing(long projectId, UpdateProjectPricingRequest request, CancellationToken cancellationToken)
        => projectService.UpdatePricingAsync(projectId, request, cancellationToken);

    [HttpPost("{projectId:long}/hold")]
    [HasPermission(PermissionCodes.ProjectChangeStatus)]
    public Task<ProjectResponse> Hold(long projectId, CancellationToken cancellationToken)
        => statusService.ChangeAsync(projectId, ProjectStatusRules.Action.Hold, cancellationToken);

    [HttpPost("{projectId:long}/resume")]
    [HasPermission(PermissionCodes.ProjectChangeStatus)]
    public Task<ProjectResponse> Resume(long projectId, CancellationToken cancellationToken)
        => statusService.ChangeAsync(projectId, ProjectStatusRules.Action.Resume, cancellationToken);

    /// <summary>Finalises the project: revenue is locked and open requirements are ticked.</summary>
    [HttpPost("{projectId:long}/complete")]
    [HasPermission(PermissionCodes.ProjectChangeStatus)]
    public Task<ProjectResponse> Complete(long projectId, CancellationToken cancellationToken)
        => statusService.ChangeAsync(projectId, ProjectStatusRules.Action.Complete, cancellationToken);

    /// <summary>Finalises the project too, unless it was already completed.</summary>
    [HttpPost("{projectId:long}/archive")]
    [HasPermission(PermissionCodes.ProjectChangeStatus)]
    public Task<ProjectResponse> Archive(long projectId, CancellationToken cancellationToken)
        => statusService.ChangeAsync(projectId, ProjectStatusRules.Action.Archive, cancellationToken);

    /// <summary>Unlocks revenue and reopens the requirements that completing ticked.</summary>
    [HttpPost("{projectId:long}/reopen")]
    [HasPermission(PermissionCodes.ProjectChangeStatus)]
    public Task<ProjectResponse> Reopen(long projectId, CancellationToken cancellationToken)
        => statusService.ChangeAsync(projectId, ProjectStatusRules.Action.Reopen, cancellationToken);
}
