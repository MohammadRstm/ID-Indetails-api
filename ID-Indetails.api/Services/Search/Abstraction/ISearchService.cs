using ID_Indetails.api.DTO.Request.Search;
using ID_Indetails.api.DTO.Response.Search;

namespace ID_Indetails.api.Services.Search.Abstraction;

public interface ISearchService
{
    Task<SearchResponse> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>The sidebar's counts, in one call: the three list totals it would otherwise ask for one by one.</summary>
    Task<NavCountsResponse> CountsAsync(NavCountsQuery query, CancellationToken cancellationToken = default);
}
