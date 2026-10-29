using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Materials.Abstraction;

/// <summary>A live material with the name of who recorded it.</summary>
public sealed record MaterialRecord(ProjectMaterial Material, string AddedByName);

public interface IMaterialRepository
{
    /// <summary>Every live material of the project, newest first, not tracked.</summary>
    Task<IReadOnlyList<MaterialRecord>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>A live material of the project, tracked.</summary>
    Task<ProjectMaterial?> GetForUpdateAsync(long projectId, long materialId, CancellationToken cancellationToken = default);

    /// <summary>The given live materials, tracked.</summary>
    Task<IReadOnlyList<ProjectMaterial>> GetManyForUpdateAsync(IReadOnlyCollection<long> materialIds, CancellationToken cancellationToken = default);

    /// <summary>The given materials, in the order of <paramref name="materialIds"/>, not tracked.</summary>
    Task<IReadOnlyList<MaterialRecord>> GetRowsAsync(IReadOnlyList<long> materialIds, CancellationToken cancellationToken = default);

    /// <summary>The materials <paramref name="addedById"/> added with these client refs, removed ones included. Not tracked.</summary>
    Task<IReadOnlyList<ProjectMaterial>> ByClientRefsAsync(long addedById, IReadOnlyCollection<Guid> clientRefs, CancellationToken cancellationToken = default);

    void Add(ProjectMaterial material);
}
