using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.Services.Expenses.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Each business's expense categories: anyone who sees the business reads them (the expense forms list them); changing
/// them takes <c>financial:write</c>. A business the caller can't see reads as 404.
/// </summary>
[ApiController]
[Route("api/expense-categories")]
[Authorize]
public sealed class ExpenseCategoriesController(IExpenseCategoryService categories) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ExpenseCategoryResponse>> List([FromQuery] string domain, CancellationToken cancellationToken)
        => categories.ListAsync(domain, cancellationToken);

    [HttpPost]
    [HasPermission(PermissionCodes.FinancialWrite)]
    public async Task<ActionResult<ExpenseCategoryResponse>> Add(AddExpenseCategoryRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await categories.AddAsync(request, cancellationToken));

    [HttpPut("{id:long}")]
    [HasPermission(PermissionCodes.FinancialWrite)]
    public Task<ExpenseCategoryResponse> Rename(long id, RenameExpenseCategoryRequest request, CancellationToken cancellationToken)
        => categories.RenameAsync(id, request, cancellationToken);

    [HttpPut("order")]
    [HasPermission(PermissionCodes.FinancialWrite)]
    public Task<IReadOnlyList<ExpenseCategoryResponse>> Order(OrderExpenseCategoriesRequest request, CancellationToken cancellationToken)
        => categories.OrderAsync(request, cancellationToken);

    [HttpDelete("{id:long}")]
    [HasPermission(PermissionCodes.FinancialWrite)]
    public async Task<IActionResult> Remove(long id, CancellationToken cancellationToken)
    {
        await categories.RemoveAsync(id, cancellationToken);
        return NoContent();
    }
}
