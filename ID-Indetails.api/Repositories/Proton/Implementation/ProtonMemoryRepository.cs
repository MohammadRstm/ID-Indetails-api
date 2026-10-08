using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Proton.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Proton.Implementation;

public sealed class ProtonMemoryRepository(IdIndetailsDbContext db) : IProtonMemoryRepository
{
    public void Add(ProtonMemory memory) => db.ProtonMemories.Add(memory);

    public async Task<IReadOnlyList<ProtonMemory>> MineAsync(long userId, CancellationToken cancellationToken = default)
        => await db.ProtonMemories.AsNoTracking().Where(m => m.UserId == userId).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync(cancellationToken);

    public async Task<bool> DeleteAsync(long userId, long id, CancellationToken cancellationToken = default)
        => await db.ProtonMemories.Where(m => m.UserId == userId && m.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
}
