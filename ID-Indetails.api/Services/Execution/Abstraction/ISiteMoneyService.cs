using ID_Indetails.api.DTO.Response.Execution;

namespace ID_Indetails.api.Services.Execution.Abstraction;

/// <summary>How a site's cash moved over time, and how long its profit would last.</summary>
public interface ISiteMoneyService
{
    Task<SiteMoneyTimelineResponse> TimelineAsync(long projectId, CancellationToken cancellationToken = default);
}
