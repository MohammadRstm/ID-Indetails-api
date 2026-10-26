using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
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

    /// <summary>Design: client work or internal, and the client.</summary>
    [HttpPut("{projectId:long}/billing")]
    [HasPermission(PermissionCodes.ProjectEdit)]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<ProjectResponse> UpdateBilling(long projectId, UpdateProjectBillingRequest request, CancellationToken cancellationToken)
        => projectService.UpdateBillingAsync(projectId, request, cancellationToken);

    /// <summary>Design: a client project's customer payment, with why it changes.</summary>
    [HttpPut("{projectId:long}/payment")]
    [HasPermission(PermissionCodes.ProjectEdit)]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<ProjectResponse> UpdatePayment(long projectId, UpdateProjectPaymentRequest request, CancellationToken cancellationToken)
        => projectService.UpdatePaymentAsync(projectId, request, cancellationToken);

    /// <summary>Execution: the employees who oversee the site.</summary>
    [HttpPut("{projectId:long}/staff")]
    [HasPermission(PermissionCodes.ProjectEdit)]
    public Task<ProjectResponse> UpdateStaff(long projectId, UpdateProjectStaffRequest request, CancellationToken cancellationToken)
        => projectService.UpdateStaffAsync(projectId, request, cancellationToken);

    /// <summary>Every change of the project's agreed payment, newest first, with why it changed.</summary>
    [HttpGet("{projectId:long}/payment-changes")]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<IReadOnlyList<ProjectPaymentChangeResponse>> PaymentChanges(long projectId, CancellationToken cancellationToken)
        => projectService.PaymentChangesAsync(projectId, cancellationToken);

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

    /// <summary>
    /// Finalises the project: revenue is locked and open requirements are ticked. An Execution project's money is
    /// removed from the box or kept (<c>box=remove|keep</c>, money holders only), or else waits for a decision.
    /// </summary>
    [HttpPost("{projectId:long}/complete")]
    [HasPermission(PermissionCodes.ProjectChangeStatus)]
    public Task<ProjectResponse> Complete(long projectId, [FromQuery] BoxChoice? box, CancellationToken cancellationToken)
        => statusService.ChangeAsync(projectId, ProjectStatusRules.Action.Complete, box, cancellationToken);

    /// <summary>Finalises the project too, unless it was already completed.</summary>
    [HttpPost("{projectId:long}/archive")]
    [HasPermission(PermissionCodes.ProjectChangeStatus)]
    public Task<ProjectResponse> Archive(long projectId, [FromQuery] BoxChoice? box, CancellationToken cancellationToken)
        => statusService.ChangeAsync(projectId, ProjectStatusRules.Action.Archive, box, cancellationToken);

    /// <summary>
    /// Unlocks revenue and reopens the requirements that completing ticked. A removed Execution project puts its
    /// earlier revenue back in the box (<c>box=readd</c>, money holders only) or counts afresh.
    /// </summary>
    [HttpPost("{projectId:long}/reopen")]
    [HasPermission(PermissionCodes.ProjectChangeStatus)]
    public Task<ProjectResponse> Reopen(long projectId, [FromQuery] BoxChoice? box, CancellationToken cancellationToken)
        => statusService.ChangeAsync(projectId, ProjectStatusRules.Action.Reopen, box, cancellationToken);
}
