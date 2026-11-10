using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Company.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Company.Implementation;

public sealed class AccountLabelRepository(IdIndetailsDbContext db) : IAccountLabelRepository
{
    public async Task<IReadOnlyList<AccountLabel>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default)
        => await db.AccountLabels
            .Where(l => includeArchived || l.ArchivedAt == null)
            .OrderBy(l => l.Position)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);

    public Task<AccountLabel?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.AccountLabels.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<bool> NameTakenAsync(string name, long? exceptId, CancellationToken cancellationToken = default)
        => db.AccountLabels.AnyAsync(l => l.Name.ToLower() == name.ToLower() && l.Id != exceptId, cancellationToken);

    public async Task<IReadOnlySet<long>> UsedIdsAsync(CancellationToken cancellationToken = default)
    {
        var payments = await db.Payments.Where(p => p.AccountLabelId != null).Select(p => p.AccountLabelId!.Value).Distinct().ToListAsync(cancellationToken);
        var expenses = await db.SiteExpenses.Where(e => e.AccountLabelId != null).Select(e => e.AccountLabelId!.Value).Distinct().ToListAsync(cancellationToken);
        return payments.Concat(expenses).ToHashSet();
    }

    public async Task<bool> InUseAsync(long id, CancellationToken cancellationToken = default)
        => await db.Payments.AnyAsync(p => p.AccountLabelId == id, cancellationToken)
            || await db.SiteExpenses.AnyAsync(e => e.AccountLabelId == id, cancellationToken);

    public void Add(AccountLabel label) => db.AccountLabels.Add(label);

    public void Remove(AccountLabel label) => db.AccountLabels.Remove(label);
}
