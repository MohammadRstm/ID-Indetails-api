using ID_Indetails.api.Data;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Execution.Implementation;

public sealed class AccountFiguresRepository(IdIndetailsDbContext db) : IAccountFiguresRepository
{
    public async Task<IReadOnlyList<AccountPaymentRow>> LivePaymentsAsync(long projectId, CancellationToken cancellationToken = default)
        => await db.Payments
            .AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.VoidedAt == null)
            .Select(p => new AccountPaymentRow(p.AccountLabelId, p.AmountUsd))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AccountExpenseRow>> SiteExpensesAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var rows = await db.SiteExpenses
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.DeletedAt == null)
            .Select(e => new { e.Id, e.AddedAt, e.AccountLabelId, e.Category, e.Supplier, e.Currency, e.Total, e.SpentOn })
            .ToListAsync(cancellationToken);
        return rows
            .OrderByDescending(e => e.AddedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new AccountExpenseRow(e.AccountLabelId, e.Category, e.Supplier, e.Currency, e.Total, e.SpentOn))
            .ToList();
    }
}
