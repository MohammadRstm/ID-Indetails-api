using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.SiteExpenses.Abstraction;

/// <summary>A live expense with the name of who recorded it and the account label it is filed under.</summary>
public sealed record SiteExpenseRecord(SiteExpense Expense, string AddedByName, string? AccountLabelName);

public interface ISiteExpenseRepository
{
    /// <summary>Every live expense of the project, newest first, not tracked.</summary>
    Task<IReadOnlyList<SiteExpenseRecord>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>A live expense of the project, tracked.</summary>
    Task<SiteExpense?> GetForUpdateAsync(long projectId, long expenseId, CancellationToken cancellationToken = default);

    /// <summary>The given expenses, in the order of <paramref name="expenseIds"/>, not tracked.</summary>
    Task<IReadOnlyList<SiteExpenseRecord>> GetRowsAsync(IReadOnlyList<long> expenseIds, CancellationToken cancellationToken = default);

    /// <summary>The expenses <paramref name="addedById"/> added with these client refs, removed ones included. Not tracked.</summary>
    Task<IReadOnlyList<SiteExpense>> ByClientRefsAsync(long addedById, IReadOnlyCollection<Guid> clientRefs, CancellationToken cancellationToken = default);

    void Add(SiteExpense expense);
}
