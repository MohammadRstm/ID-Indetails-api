using ID_Indetails.api.Authorization;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// A signed-in client reading a project's published presentation (client portal token only). Anything they may not
/// see answers 404.
/// </summary>
[ApiController]
[Route("api/portal/projects/{projectId:long}")]
[Authorize(Policy = AuthorizationPolicies.Portal)]
public sealed class PortalProjectsController(IPortalPresentationService presentation) : ControllerBase
{
    /// <summary>What is published: hero, introduction and the three sections. Before the first publish, empty.</summary>
    [HttpGet]
    public Task<PortalPresentationResponse> Get(long projectId, CancellationToken cancellationToken)
        => presentation.GetAsync(projectId, cancellationToken);

    /// <summary>A five-minute link to open (images, videos, PDFs, when <paramref name="inline"/>) or download one published file.</summary>
    [HttpGet("items/{itemId:long}/link")]
    public Task<PortalContentLinkResponse> Link(long projectId, long itemId, [FromQuery] bool inline, CancellationToken cancellationToken)
        => presentation.LinkAsync(projectId, itemId, inline, cancellationToken);
}
