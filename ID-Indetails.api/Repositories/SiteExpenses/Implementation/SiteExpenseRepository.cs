using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.SiteExpenses.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.SiteExpenses.Implementation;

public sealed class SiteExpenseRepository(IdIndetailsDbContext db) : ISiteExpenseRepository
{
    public async Task<IReadOnlyList<SiteExpenseRecord>> ListAsync(long projectId, CancellationToken cancellationToken = default)
        => (await Rows(db.SiteExpenses.Where(e => e.ProjectId == projectId && e.DeletedAt == null))
                .ToListAsync(cancellationToken))
            .OrderByDescending(r => r.Expense.AddedAt)
            .ThenByDescending(r => r.Expense.Id)
            .ToList();

    public Task<SiteExpense?> GetForUpdateAsync(long projectId, long expenseId, CancellationToken cancellationToken = default)
        => db.SiteExpenses.SingleOrDefaultAsync(
            e => e.Id == expenseId && e.ProjectId == projectId && e.DeletedAt == null, cancellationToken);

    public async Task<IReadOnlyList<SiteExpenseRecord>> GetRowsAsync(IReadOnlyList<long> expenseIds, CancellationToken cancellationToken = default)
    {
        var rows = (await Rows(db.SiteExpenses.Where(e => expenseIds.Contains(e.Id))).ToListAsync(cancellationToken))
            .ToDictionary(r => r.Expense.Id);
        return expenseIds.Select(id => rows[id]).ToList();
    }

    public async Task<IReadOnlyList<SiteExpense>> ByClientRefsAsync(
        long addedById,
        IReadOnlyCollection<Guid> clientRefs,
        CancellationToken cancellationToken = default)
    {
        var refs = clientRefs.Select(r => (Guid?)r).ToList();
        return await db.SiteExpenses.AsNoTracking()
            .Where(e => e.AddedById == addedById && refs.Contains(e.ClientRef))
            .ToListAsync(cancellationToken);
    }

    public void Add(SiteExpense expense) => db.SiteExpenses.Add(expense);

    private IQueryable<SiteExpenseRecord> Rows(IQueryable<SiteExpense> expenses)
        => from e in expenses.AsNoTracking()
           join u in db.Users on e.AddedById equals u.Id
           join l in db.AccountLabels on e.AccountLabelId equals l.Id into labels
           from label in labels.DefaultIfEmpty()
           select new SiteExpenseRecord(e, (u.FirstName + " " + u.LastName).Trim(), label == null ? null : label.Name);
}
