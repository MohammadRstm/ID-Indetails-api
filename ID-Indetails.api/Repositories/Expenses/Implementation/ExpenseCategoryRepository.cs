using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Expenses.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Expenses.Implementation;

public sealed class ExpenseCategoryRepository(IdIndetailsDbContext db) : IExpenseCategoryRepository
{
    public async Task<IReadOnlyList<ExpenseCategory>> ListAsync(string domain, CancellationToken cancellationToken = default)
        => await db.ExpenseCategories.AsNoTracking()
            .Where(c => c.DomainCode == domain)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

    public Task<ExpenseCategory?> GetAsync(long id, CancellationToken cancellationToken = default)
        => db.ExpenseCategories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ExpenseCategory>> ListForUpdateAsync(string domain, CancellationToken cancellationToken = default)
        => await db.ExpenseCategories
            .Where(c => c.DomainCode == domain)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

    public async Task<int> UsesAsync(string domain, string code, CancellationToken cancellationToken = default)
    {
        // budget lines name categories on Execution sites only (Design's budgets have departments and "expenses")
        var lines = domain == DomainCodes.Execution
            ? await (from l in db.ProjectBudgetLines
                     join b in db.ProjectBudgets on l.BudgetId equals b.Id
                     join p in db.Projects on b.ProjectId equals p.Id
                     where l.Category == code && p.DomainCode == domain
                     select l.Id).CountAsync(cancellationToken)
            : 0;
        var expenses = domain == DomainCodes.Execution
            ? await db.SiteExpenses.CountAsync(e => e.Category == code && e.DeletedAt == null, cancellationToken)
            : await db.ProjectExpenses.CountAsync(e => e.Category == code && e.DeletedAt == null && e.Project.DomainCode == domain, cancellationToken);
        return lines + expenses;
    }

    public void Add(ExpenseCategory category) => db.ExpenseCategories.Add(category);

    public void Remove(ExpenseCategory category) => db.ExpenseCategories.Remove(category);
}
