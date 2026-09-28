using ID_Indetails.api.Data;
using ID_Indetails.api.Repositories.Requirements.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Requirements.Implementation;

public sealed class RequirementRepository(IdIndetailsDbContext db) : IRequirementRepository
{
    public async Task<(int Total, int Done)> CountAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var counts = await db.ProjectRequirements
            .Where(r => r.ProjectId == projectId && r.DeletedAt == null)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Done = g.Count(r => r.IsDone) })
            .SingleOrDefaultAsync(cancellationToken);

        return counts is null ? (0, 0) : (counts.Total, counts.Done);
    }

    public Task AutoTickOpenAsync(long projectId, DateTime doneAt, long doneById, CancellationToken cancellationToken = default)
        => db.ProjectRequirements
            .Where(r => r.ProjectId == projectId && r.DeletedAt == null && !r.IsDone)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsDone, true)
                .SetProperty(r => r.AutoDone, true)
                .SetProperty(r => r.DoneAt, doneAt)
                .SetProperty(r => r.DoneById, doneById), cancellationToken);

    public Task UntickAutoDoneAsync(long projectId, CancellationToken cancellationToken = default)
        => db.ProjectRequirements
            .Where(r => r.ProjectId == projectId && r.DeletedAt == null && r.AutoDone)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsDone, false)
                .SetProperty(r => r.AutoDone, false)
                .SetProperty(r => r.DoneAt, (DateTime?)null)
                .SetProperty(r => r.DoneById, (long?)null), cancellationToken);
}
