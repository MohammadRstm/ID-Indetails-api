using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Expenses.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Expenses.Implementation;

public sealed class ExpenseRepository(IdIndetailsDbContext db) : IExpenseRepository
{
    public async Task<IReadOnlyList<ProjectExpense>> ListAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.ProjectExpenses
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.DeletedAt == null)
            .OrderByDescending(x => x.SpentOn)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(string Currency, decimal Amount)>> AmountsAsync(long projectId, CancellationToken cancellationToken = default)
        => (await db.ProjectExpenses
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.DeletedAt == null)
                .Select(x => new { x.Currency, x.Amount })
                .ToListAsync(cancellationToken))
            .Select(x => (x.Currency, x.Amount))
            .ToList();

    public Task<ProjectExpense?> GetForUpdateAsync(long projectId, long expenseId, CancellationToken cancellationToken = default)
        => db.ProjectExpenses.SingleOrDefaultAsync(
            x => x.Id == expenseId && x.ProjectId == projectId && x.DeletedAt == null, cancellationToken);

    public void Add(ProjectExpense expense) => db.ProjectExpenses.Add(expense);
}
