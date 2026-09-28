using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Materials.Abstraction;

/// <summary>A live material with the name of who recorded it and whether it is on a client statement.</summary>
public sealed record MaterialRecord(ProjectMaterial Material, string AddedByName, bool IsStated);

public interface IMaterialRepository
{
    /// <summary>Every live material of the project, newest first, not tracked.</summary>
    Task<IReadOnlyList<MaterialRecord>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>A live material of the project, tracked.</summary>
    Task<ProjectMaterial?> GetForUpdateAsync(long projectId, long materialId, CancellationToken cancellationToken = default);

    /// <summary>Whether the material is on any client statement.</summary>
    Task<bool> IsStatedAsync(long materialId, CancellationToken cancellationToken = default);

    /// <summary>The given materials, in the order of <paramref name="materialIds"/>, not tracked.</summary>
    Task<IReadOnlyList<MaterialRecord>> GetRowsAsync(IReadOnlyList<long> materialIds, CancellationToken cancellationToken = default);

    void Add(ProjectMaterial material);
}
