using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.DTO.Response.Expenses;

namespace ID_Indetails.api.Services.Expenses.Abstraction;

public interface IExpenseService
{
    Task<ExpenseListResponse> ListAsync(long projectId, CancellationToken cancellationToken = default);

    Task<ExpenseResponse> CreateAsync(long projectId, CreateExpenseRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(long projectId, long expenseId, CancellationToken cancellationToken = default);
}
