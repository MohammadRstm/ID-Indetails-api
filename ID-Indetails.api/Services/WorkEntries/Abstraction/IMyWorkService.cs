using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;

namespace ID_Indetails.api.Services.WorkEntries.Abstraction;

/// <summary>A person's own view of their Design work, and the same summary for people who see everyone's work.</summary>
public interface IMyWorkService
{
    Task<WorkSummaryResponse> SummaryForMeAsync(WorkSummaryQuery query, CancellationToken cancellationToken = default);

    Task<WorkSummaryResponse> SummaryForPersonAsync(long userId, WorkSummaryQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MyProjectResponse>> MyProjectsAsync(CancellationToken cancellationToken = default);

    Task<MyProjectWorkResponse> MyProjectWorkAsync(long projectId, CancellationToken cancellationToken = default);
}
