using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Expenses.Abstraction;

public interface IExpenseRepository
{
    /// <summary>Live expenses of the project, newest first, not tracked.</summary>
    Task<IReadOnlyList<ProjectExpense>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Currency and amount of each live expense, for totals.</summary>
    Task<IReadOnlyList<(string Currency, decimal Amount)>> AmountsAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>A live expense of the project, tracked.</summary>
    Task<ProjectExpense?> GetForUpdateAsync(long projectId, long expenseId, CancellationToken cancellationToken = default);

    void Add(ProjectExpense expense);
}
