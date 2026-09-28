using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Company.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Company.Implementation;

public sealed class OverheadRepository(IdIndetailsDbContext db) : IOverheadRepository
{
    public async Task<IReadOnlyList<OverheadItem>> ListAsync(CancellationToken cancellationToken = default)
        => await db.OverheadItems.AsNoTracking().Include(o => o.Amounts).ToListAsync(cancellationToken);

    public Task<OverheadItem?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.OverheadItems.Include(o => o.Amounts).SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(OverheadItem item) => db.OverheadItems.Add(item);

    public void AddAmount(OverheadAmount amount) => db.OverheadAmounts.Add(amount);

    public void Remove(OverheadItem item) => db.OverheadItems.Remove(item);
}
