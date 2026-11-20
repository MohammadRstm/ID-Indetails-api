using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Services.Projects.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// A project's cost budget: per department and expenses on Design, per expense category and ground labour on
/// Execution, in USD net of TVA, kept in versions with their reasons, against the cost so far.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:long}/budget")]
public sealed class ProjectBudgetsController(IProjectBudgetService budgets) : ControllerBase
{
    /// <summary>The current version line by line (amount, cost so far, left), the cost no line holds, and every version.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<ProjectBudgetResponse> Get(long projectId, CancellationToken cancellationToken)
        => budgets.GetAsync(projectId, cancellationToken);

    /// <summary>
    /// Sets the next version, with why. <c>financial:write</c> on Design, <c>project:pricing_manage</c> on Execution
    /// (403 otherwise). 400 <c>budget_line_invalid</c> for a department not on the project or a category of the other business.
    /// </summary>
    [HttpPut]
    [HasAnyPermission(PermissionCodes.FinancialWrite, PermissionCodes.ProjectPricingManage)]
    public Task<ProjectBudgetResponse> Set(long projectId, SetProjectBudgetRequest request, CancellationToken cancellationToken)
        => budgets.SetAsync(projectId, request, cancellationToken);
}
