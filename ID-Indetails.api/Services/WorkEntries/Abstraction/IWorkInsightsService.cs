using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.WorkEntries;

namespace ID_Indetails.api.Services.WorkEntries.Abstraction;

/// <summary>Everyone's Design work by project and by person, for people who see all of it.</summary>
public interface IWorkInsightsService
{
    Task<ProjectWorkResponse> ProjectWorkAsync(long projectId, ProjectWorkQuery query, CancellationToken cancellationToken = default);

    Task<ProjectContributorDetailResponse> ContributorAsync(long projectId, long userId, ProjectWorkQuery query, CancellationToken cancellationToken = default);

    Task<ProjectTimelineResponse> TimelineAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>The entries that start in a range of days, in time order, for the Timeline tab's lanes and one-day view.</summary>
    Task<IReadOnlyList<TimelineEntryResponse>> TimelineEntriesAsync(
        long projectId,
        TimelineEntriesQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Cost and payments week by week, for the project's money charts and report. Any domain.</summary>
    Task<ProjectCostTimelineResponse> CostTimelineAsync(long projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersonWorkResponse>> PeopleAsync(PeopleWorkQuery query, CancellationToken cancellationToken = default);

    Task<EmployeeLabourCostResponse> PersonCostAsync(long userId, CancellationToken cancellationToken = default);
}
