using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Company.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Company.Implementation;

public sealed class OverheadRepository(IdIndetailsDbContext db) : IOverheadRepository
{
    public async Task<IReadOnlyList<(OverheadGroup Group, int Lines)>> GroupsAsync(CancellationToken cancellationToken = default)
        => (await db.OverheadGroups
                .AsNoTracking()
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Name)
                .Select(g => new { Group = g, Lines = db.OverheadItems.Count(o => o.GroupId == g.Id) })
                .ToListAsync(cancellationToken))
            .Select(x => (x.Group, x.Lines))
            .ToList();

    public Task<OverheadGroup?> GetGroupForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.OverheadGroups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);

    public Task<bool> GroupNameTakenAsync(string name, long? exceptId, CancellationToken cancellationToken = default)
        => db.OverheadGroups.AnyAsync(g => g.Name == name && g.Id != exceptId, cancellationToken);

    public Task<int> LinesInGroupAsync(long id, CancellationToken cancellationToken = default)
        => db.OverheadItems.CountAsync(o => o.GroupId == id, cancellationToken);

    public void AddGroup(OverheadGroup group) => db.OverheadGroups.Add(group);

    public void RemoveGroup(OverheadGroup group) => db.OverheadGroups.Remove(group);

    public async Task<IReadOnlyList<OverheadItem>> ListAsync(CancellationToken cancellationToken = default)
        => await db.OverheadItems.AsNoTracking().Include(o => o.Amounts).Include(o => o.Group).ToListAsync(cancellationToken);

    public Task<OverheadItem?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.OverheadItems.Include(o => o.Amounts).Include(o => o.Group).SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(OverheadItem item) => db.OverheadItems.Add(item);

    public void AddAmount(OverheadAmount amount) => db.OverheadAmounts.Add(amount);

    public void Remove(OverheadItem item) => db.OverheadItems.Remove(item);
}
