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

    public void Add(AccountLabel label) => db.AccountLabels.Add(label);
}
