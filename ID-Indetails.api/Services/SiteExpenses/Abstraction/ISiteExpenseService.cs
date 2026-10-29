using ID_Indetails.api.DTO.Request.SiteExpenses;
using ID_Indetails.api.DTO.Response.SiteExpenses;

namespace ID_Indetails.api.Services.SiteExpenses.Abstraction;

/// <summary>Expenses of Execution sites in the caller's visible domains.</summary>
public interface ISiteExpenseService
{
    Task<SiteExpenseListResponse> ListAsync(long projectId, SiteExpenseListQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SiteExpenseResponse>> AddAsync(long projectId, AddSiteExpensesRequest request, CancellationToken cancellationToken = default);

    Task<SiteExpenseResponse> UpdateAsync(long projectId, long expenseId, SiteExpenseEntryRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(long projectId, long expenseId, CancellationToken cancellationToken = default);
}
