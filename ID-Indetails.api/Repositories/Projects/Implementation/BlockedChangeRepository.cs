using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Projects.Implementation;

public sealed class BlockedChangeRepository(IdIndetailsDbContext db) : IBlockedChangeRepository
{
    public async Task<BlockedChangeRow?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        // untracked: deciding updates the row in the database directly, so a tracked copy would read stale
        var row = await db.BlockedChanges
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new
            {
                Change = c,
                c.Project,
                RequestedBy = db.Users.Where(u => u.Id == c.RequestedById).Select(u => new { u.FirstName, u.LastName, u.Email }).First(),
                DecidedBy = db.Users.Where(u => u.Id == c.DecidedById).Select(u => new { u.FirstName, u.LastName, u.Email }).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new BlockedChangeRow(
                row.Change,
                row.Project,
                PersonName.Of(row.RequestedBy.FirstName, row.RequestedBy.LastName, row.RequestedBy.Email),
                row.DecidedBy is { } decider ? PersonName.Of(decider.FirstName, decider.LastName, decider.Email) : null);
    }

    public async Task<bool> DecideAsync(long id, BlockedChangeStatus status, long deciderId, DateTime at, CancellationToken cancellationToken = default)
        => await db.BlockedChanges
            .Where(c => c.Id == id && c.Status == BlockedChangeStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.DecidedById, deciderId)
                .SetProperty(c => c.DecidedAt, at), cancellationToken) == 1;

    public Task ReopenAsync(long id, CancellationToken cancellationToken = default)
        => db.BlockedChanges
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, BlockedChangeStatus.Pending)
                .SetProperty(c => c.DecidedById, (long?)null)
                .SetProperty(c => c.DecidedAt, (DateTime?)null), cancellationToken);

    public Task SetResultAsync(long id, long resultId, CancellationToken cancellationToken = default)
        => db.BlockedChanges.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ResultId, resultId), cancellationToken);

    public void Add(BlockedChange change) => db.BlockedChanges.Add(change);
}
