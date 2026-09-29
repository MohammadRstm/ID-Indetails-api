using ID_Indetails.api.DTO.Request.Search;
using ID_Indetails.api.DTO.Response.Search;
using ID_Indetails.api.Services.Search.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Search across projects, clients and people, limited to what the caller may list.</summary>
[ApiController]
[Route("api/search")]
[Authorize]
public sealed class SearchController(ISearchService searchService) : ControllerBase
{
    [HttpGet]
    public Task<SearchResponse> Search([FromQuery] SearchQuery query, CancellationToken cancellationToken)
        => searchService.SearchAsync(query, cancellationToken);
}
