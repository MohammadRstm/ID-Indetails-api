using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Services.Projects.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// A Design project's departments (teams): who works on it, each one's share of the price and, for whoever sees the
/// money, each one's budget and labour. Execution sites have none (<c>422 not_for_this_domain</c>).
/// </summary>
[ApiController]
[Route("api/projects/{projectId:long}/departments")]
[HasPermission(PermissionCodes.ProjectView)]
public sealed class ProjectDepartmentsController(IProjectDepartmentService departments) : ControllerBase
{
    /// <summary>The departments, and "Full file" when every one works on it; shares and money with <c>financial:view</c>.</summary>
    [HttpGet]
    public Task<ProjectDepartmentsResponse> Get(long projectId, CancellationToken cancellationToken)
        => departments.GetAsync(projectId, cancellationToken);

    /// <summary>Every change of the split, newest first.</summary>
    [HttpGet("history")]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<IReadOnlyList<ProjectDepartmentChangeResponse>> History(long projectId, CancellationToken cancellationToken)
        => departments.HistoryAsync(projectId, cancellationToken);

    /// <summary>
    /// Sets the departments: the shares go back to equal, and hours waiting for an added one go through. 422
    /// <c>department_has_hours</c> for taking off one with hours logged on the project.
    /// </summary>
    [HttpPut]
    [HasPermission(PermissionCodes.ProjectEdit)]
    public Task<ProjectDepartmentsResponse> Set(long projectId, SetProjectDepartmentsRequest request, CancellationToken cancellationToken)
        => departments.SetAsync(projectId, request, cancellationToken);

    /// <summary>Sets each department's share, adding up to 100%, with why.</summary>
    [HttpPut("shares")]
    [HasPermission(PermissionCodes.FinancialWrite)]
    public Task<ProjectDepartmentsResponse> SetShares(long projectId, SetDepartmentSharesRequest request, CancellationToken cancellationToken)
        => departments.SetSharesAsync(projectId, request, cancellationToken);
}
