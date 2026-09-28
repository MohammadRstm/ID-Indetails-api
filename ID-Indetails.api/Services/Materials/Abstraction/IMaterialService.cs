using ID_Indetails.api.DTO.Request.Materials;
using ID_Indetails.api.DTO.Response.Materials;

namespace ID_Indetails.api.Services.Materials.Abstraction;

/// <summary>Materials of Execution sites in the caller's visible domains.</summary>
public interface IMaterialService
{
    Task<MaterialListResponse> ListAsync(long projectId, MaterialListQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaterialResponse>> AddAsync(long projectId, AddMaterialsRequest request, CancellationToken cancellationToken = default);

    Task<MaterialResponse> UpdateAsync(long projectId, long materialId, MaterialEntryRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(long projectId, long materialId, CancellationToken cancellationToken = default);

    Task<MaterialResponse> SetPriceAsync(long projectId, long materialId, SetMaterialPriceRequest request, CancellationToken cancellationToken = default);
}
