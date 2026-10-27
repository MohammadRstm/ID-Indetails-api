using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Execution;

namespace ID_Indetails.api.Services.Execution.Abstraction;

/// <summary>Views of what site staff recorded: project cards and each person's records.</summary>
public interface ISiteRecordsService
{
    /// <summary>The visible Execution sites among the ids; others are left out.</summary>
    Task<IReadOnlyList<SiteSummaryResponse>> SummariesAsync(SiteSummaryQuery query, CancellationToken cancellationToken = default);

    Task<SiteRecordsResponse> MineAsync(SiteRecordsQuery query, CancellationToken cancellationToken = default);

    Task<SiteRecordsResponse> OfAsync(long userId, SiteRecordsQuery query, CancellationToken cancellationToken = default);
}
