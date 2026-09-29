using ID_Indetails.api.DTO.Request.Search;
using ID_Indetails.api.DTO.Response.Search;

namespace ID_Indetails.api.Services.Search.Abstraction;

public interface ISearchService
{
    Task<SearchResponse> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
}
