using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Expenses.Abstraction;

public interface IExpenseCategoryRepository
{
    /// <summary>The business's categories in their order. Not tracked.</summary>
    Task<IReadOnlyList<ExpenseCategory>> ListAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>Tracked.</summary>
    Task<ExpenseCategory?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, in their order.</summary>
    Task<IReadOnlyList<ExpenseCategory>> ListForUpdateAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>How many of the business's expenses (removed ones aside) and budget lines use the code.</summary>
    Task<int> UsesAsync(string domain, string code, CancellationToken cancellationToken = default);

    void Add(ExpenseCategory category);

    void Remove(ExpenseCategory category);
}
