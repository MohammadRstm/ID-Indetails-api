using ID_Indetails.api.DTO.Request.Labour;
using ID_Indetails.api.DTO.Response.Labour;

namespace ID_Indetails.api.Services.Labour.Abstraction;

/// <summary>Ground labour of Execution sites in the caller's visible domains.</summary>
public interface ILabourService
{
    Task<LabourWeekListResponse> ListWeeksAsync(long projectId, CancellationToken cancellationToken = default);

    Task<LabourWeekResponse> GetWeekAsync(long projectId, DateOnly weekStart, CancellationToken cancellationToken = default);

    Task<LabourWeekResponse> SubmitWeekAsync(long projectId, DateOnly weekStart, SubmitLabourWeekRequest request, CancellationToken cancellationToken = default);

    Task<LabourDefaultsResponse> GetDefaultsAsync(long projectId, CancellationToken cancellationToken = default);

    Task<LabourDefaultsResponse> SetDefaultsAsync(long projectId, SetLabourDefaultsRequest request, CancellationToken cancellationToken = default);
}
