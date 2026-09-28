using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Requirements;
using ID_Indetails.api.DTO.Response.Requirements;
using ID_Indetails.api.Services.Requirements.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>A project's requirements. Projects of a domain the caller can't see read as 404.</summary>
[ApiController]
[Route("api/projects/{projectId:long}/requirements")]
public sealed class RequirementsController(IRequirementService requirementService) : ControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RequirementView)]
    public Task<RequirementListResponse> List(long projectId, CancellationToken cancellationToken)
        => requirementService.ListAsync(projectId, cancellationToken);

    [HttpPost]
    [HasPermission(PermissionCodes.RequirementManage)]
    public async Task<ActionResult<RequirementResponse>> Create(long projectId, SaveRequirementRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await requirementService.CreateAsync(projectId, request, cancellationToken));

    /// <summary>Replaces the text, deadline and assignees.</summary>
    [HttpPut("{requirementId:long}")]
    [HasPermission(PermissionCodes.RequirementManage)]
    public Task<RequirementResponse> Update(long projectId, long requirementId, SaveRequirementRequest request, CancellationToken cancellationToken)
        => requirementService.UpdateAsync(projectId, requirementId, request, cancellationToken);

    [HttpPost("{requirementId:long}/done")]
    [HasPermission(PermissionCodes.RequirementManage)]
    public Task<RequirementResponse> Done(long projectId, long requirementId, CancellationToken cancellationToken)
        => requirementService.SetDoneAsync(projectId, requirementId, true, cancellationToken);

    [HttpPost("{requirementId:long}/undone")]
    [HasPermission(PermissionCodes.RequirementManage)]
    public Task<RequirementResponse> Undone(long projectId, long requirementId, CancellationToken cancellationToken)
        => requirementService.SetDoneAsync(projectId, requirementId, false, cancellationToken);

    [HttpDelete("{requirementId:long}")]
    [HasPermission(PermissionCodes.RequirementManage)]
    public async Task<IActionResult> Delete(long projectId, long requirementId, CancellationToken cancellationToken)
    {
        await requirementService.DeleteAsync(projectId, requirementId, cancellationToken);
        return NoContent();
    }
}
