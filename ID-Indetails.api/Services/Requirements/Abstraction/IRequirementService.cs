using ID_Indetails.api.DTO.Request.Requirements;
using ID_Indetails.api.DTO.Response.Requirements;

namespace ID_Indetails.api.Services.Requirements.Abstraction;

public interface IRequirementService
{
    Task<RequirementListResponse> ListAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own open requirements across every project they can see.</summary>
    Task<MyRequirementsResponse> ListMineAsync(CancellationToken cancellationToken = default);

    Task<RequirementResponse> CreateAsync(long projectId, SaveRequirementRequest request, CancellationToken cancellationToken = default);

    Task<RequirementResponse> UpdateAsync(long projectId, long requirementId, SaveRequirementRequest request, CancellationToken cancellationToken = default);

    /// <summary>Ticks or unticks a requirement. Asking for the state it is already in changes nothing.</summary>
    Task<RequirementResponse> SetDoneAsync(long projectId, long requirementId, bool done, CancellationToken cancellationToken = default);

    Task DeleteAsync(long projectId, long requirementId, CancellationToken cancellationToken = default);
}
