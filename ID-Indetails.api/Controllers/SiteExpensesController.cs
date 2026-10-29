using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.SiteExpenses;
using ID_Indetails.api.DTO.Response.SiteExpenses;
using ID_Indetails.api.Services.SiteExpenses.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// The expenses of an Execution site. Projects of a domain the caller can't see read as 404. The phone app still calls the
/// old <c>/materials</c> paths, so they answer the same way until it moves to <c>/site-expenses</c> (then delete that route).
/// </summary>
[ApiController]
[Route("api/projects/{projectId:long}/site-expenses")]
[Route("api/projects/{projectId:long}/materials")]
public sealed class SiteExpensesController(ISiteExpenseService siteExpenseService) : ControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.ProjectView)]
    public Task<SiteExpenseListResponse> List(long projectId, [FromQuery] SiteExpenseListQuery query, CancellationToken cancellationToken)
        => siteExpenseService.ListAsync(projectId, query, cancellationToken);

    [HttpPost]
    [HasPermission(PermissionCodes.MaterialCreate)]
    public async Task<ActionResult<IReadOnlyList<SiteExpenseResponse>>> Add(long projectId, AddSiteExpensesRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await siteExpenseService.AddAsync(projectId, request, cancellationToken));

    /// <summary>Your own expenses with <c>material:create</c>, anyone's with <c>material:manage</c>.</summary>
    [HttpPut("{expenseId:long}")]
    [HasAnyPermission(PermissionCodes.MaterialCreate, PermissionCodes.MaterialManage)]
    public Task<SiteExpenseResponse> Update(long projectId, long expenseId, SiteExpenseEntryRequest request, CancellationToken cancellationToken)
        => siteExpenseService.UpdateAsync(projectId, expenseId, request, cancellationToken);

    [HttpDelete("{expenseId:long}")]
    [HasAnyPermission(PermissionCodes.MaterialCreate, PermissionCodes.MaterialManage)]
    public async Task<IActionResult> Delete(long projectId, long expenseId, CancellationToken cancellationToken)
    {
        await siteExpenseService.DeleteAsync(projectId, expenseId, cancellationToken);
        return NoContent();
    }
}
