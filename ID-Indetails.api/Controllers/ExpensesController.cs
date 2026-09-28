using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.Services.Expenses.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>A project's expenses. Projects of a domain the caller can't see read as 404.</summary>
[ApiController]
[Route("api/projects/{projectId:long}/expenses")]
public sealed class ExpensesController(IExpenseService expenseService) : ControllerBase
{
    [HttpGet]
    [HasAnyPermission(PermissionCodes.ExpenseManage, PermissionCodes.FinancialView)]
    public Task<ExpenseListResponse> List(long projectId, CancellationToken cancellationToken)
        => expenseService.ListAsync(projectId, cancellationToken);

    [HttpPost]
    [HasPermission(PermissionCodes.ExpenseManage)]
    public async Task<ActionResult<ExpenseResponse>> Create(long projectId, CreateExpenseRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await expenseService.CreateAsync(projectId, request, cancellationToken));

    [HttpDelete("{expenseId:long}")]
    [HasPermission(PermissionCodes.ExpenseManage)]
    public async Task<IActionResult> Delete(long projectId, long expenseId, CancellationToken cancellationToken)
    {
        await expenseService.DeleteAsync(projectId, expenseId, cancellationToken);
        return NoContent();
    }
}
