using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.WorkEntries.Implementation;

public sealed class ReopenedMonthRepository(IdIndetailsDbContext db) : IReopenedMonthRepository
{
    public async Task<IReadOnlyList<ReopenedMonthView>> OpenAsync(DateOnly today, CancellationToken cancellationToken = default)
        => (await db.ReopenedMonths.AsNoTracking()
                .Where(m => m.OpenUntil >= today)
                .OrderBy(m => m.Month)
                .Select(m => new { m.Month, m.OpenUntil, m.Note, m.ReopenedById, m.ReopenedBy.FirstName, m.ReopenedBy.LastName, m.ReopenedBy.Email, m.ReopenedAt })
                .ToListAsync(cancellationToken))
            .Select(m => new ReopenedMonthView(m.Month, m.OpenUntil, m.Note, m.ReopenedById, PersonName.Of(m.FirstName, m.LastName, m.Email), m.ReopenedAt))
            .ToList();

    public Task<ReopenedMonth?> GetAsync(DateOnly month, CancellationToken cancellationToken = default)
        => db.ReopenedMonths.SingleOrDefaultAsync(m => m.Month == month, cancellationToken);

    public void Add(ReopenedMonth month) => db.ReopenedMonths.Add(month);

    public void Remove(ReopenedMonth month) => db.ReopenedMonths.Remove(month);
}
