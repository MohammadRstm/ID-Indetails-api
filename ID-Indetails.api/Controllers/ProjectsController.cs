using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Services.Projects.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Projects. Projects of a domain the caller can't see read as 404.</summary>
[ApiController]
[Route("api/projects")]
public sealed class ProjectsController(IProjectService projectService) : ControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.ProjectView)]
    public Task<PagedResponse<ProjectSummaryResponse>> List([FromQuery] ProjectListQuery query, CancellationToken cancellationToken)
        => projectService.ListAsync(query, cancellationToken);

    /// <summary>Money is included only for callers with financial access.</summary>
    [HttpGet("{projectId:long}")]
    [HasPermission(PermissionCodes.ProjectView)]
    public Task<ProjectResponse> Get(long projectId, CancellationToken cancellationToken)
        => projectService.GetAsync(projectId, cancellationToken);

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
}
