using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.DTO.Response.Expenses;

namespace ID_Indetails.api.Services.Expenses.Abstraction;

/// <summary>Each business's expense categories (see <c>Business/Expenses/ExpenseCategoryRules</c>).</summary>
public interface IExpenseCategoryService
{
    Task<IReadOnlyList<ExpenseCategoryResponse>> ListAsync(string domain, CancellationToken cancellationToken = default);

    Task<ExpenseCategoryResponse> AddAsync(AddExpenseCategoryRequest request, CancellationToken cancellationToken = default);

    Task<ExpenseCategoryResponse> RenameAsync(long id, RenameExpenseCategoryRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpenseCategoryResponse>> OrderAsync(OrderExpenseCategoriesRequest request, CancellationToken cancellationToken = default);

    Task RemoveAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The business's category codes in their order, for checking an expense's or a budget line's.</summary>
    Task<IReadOnlyList<string>> CodesAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>The business's category names by code.</summary>
    Task<IReadOnlyDictionary<string, string>> NamesAsync(string domain, CancellationToken cancellationToken = default);
}
