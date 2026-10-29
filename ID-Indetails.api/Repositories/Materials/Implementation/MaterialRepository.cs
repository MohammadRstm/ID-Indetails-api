using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Materials.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Materials.Implementation;

public sealed class MaterialRepository(IdIndetailsDbContext db) : IMaterialRepository
{
    public async Task<IReadOnlyList<MaterialRecord>> ListAsync(long projectId, CancellationToken cancellationToken = default)
        => (await Rows(db.ProjectMaterials.Where(m => m.ProjectId == projectId && m.DeletedAt == null))
                .ToListAsync(cancellationToken))
            .OrderByDescending(r => r.Material.AddedAt)
            .ThenByDescending(r => r.Material.Id)
            .ToList();

    public Task<ProjectMaterial?> GetForUpdateAsync(long projectId, long materialId, CancellationToken cancellationToken = default)
        => db.ProjectMaterials.SingleOrDefaultAsync(
            m => m.Id == materialId && m.ProjectId == projectId && m.DeletedAt == null, cancellationToken);

    public async Task<IReadOnlyList<ProjectMaterial>> GetManyForUpdateAsync(IReadOnlyCollection<long> materialIds, CancellationToken cancellationToken = default)
        => await db.ProjectMaterials.Where(m => materialIds.Contains(m.Id) && m.DeletedAt == null).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MaterialRecord>> GetRowsAsync(IReadOnlyList<long> materialIds, CancellationToken cancellationToken = default)
    {
        var rows = (await Rows(db.ProjectMaterials.Where(m => materialIds.Contains(m.Id))).ToListAsync(cancellationToken))
            .ToDictionary(r => r.Material.Id);
        return materialIds.Select(id => rows[id]).ToList();
    }

    public async Task<IReadOnlyList<ProjectMaterial>> ByClientRefsAsync(
        long addedById,
        IReadOnlyCollection<Guid> clientRefs,
        CancellationToken cancellationToken = default)
    {
        var refs = clientRefs.Select(r => (Guid?)r).ToList();
        return await db.ProjectMaterials.AsNoTracking()
            .Where(m => m.AddedById == addedById && refs.Contains(m.ClientRef))
            .ToListAsync(cancellationToken);
    }

    public void Add(ProjectMaterial material) => db.ProjectMaterials.Add(material);

    private IQueryable<MaterialRecord> Rows(IQueryable<ProjectMaterial> materials)
        => from m in materials.AsNoTracking()
           join u in db.Users on m.AddedById equals u.Id
           select new MaterialRecord(m, (u.FirstName + " " + u.LastName).Trim());
}
