using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Company.Abstraction;

public interface IWorkingTimeService
{
    Task<WorkingTimeResponse> GetAsync(CancellationToken cancellationToken = default);

    Task<WorkingTimeResponse> SetAsync(SetWorkingTimeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Every policy, oldest first; cached, and dropped whenever a policy changes.</summary>
    Task<IReadOnlyList<WorkingTimePolicy>> GetPoliciesAsync(CancellationToken cancellationToken = default);
}
