using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// A Design project's client portal as staff see it: the files chosen for the client (chosen in Project files), the
/// hero images and introduction, the preview and publishing. Holders of client_access:manage look at it; changing it needs
/// client_portal:manage, checked in the service. See docs/client-portal.md.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:long}/portal/content")]
[HasAnyPermission(PermissionCodes.ClientPortalManage, PermissionCodes.ClientAccessManage)]
public sealed class ProjectPortalContentController(IPortalPublishingService publishing) : ControllerBase
{
    /// <summary>The client-relevant files with their states, the hero images, introduction and publication.</summary>
    [HttpGet]
    public Task<PortalContentResponse> Get(long projectId, CancellationToken cancellationToken)
        => publishing.GetAsync(projectId, cancellationToken);

    /// <summary>A short-lived link to open (images, videos, PDFs, when <paramref name="inline"/>) or download one of the client's files.</summary>
    [HttpGet("items/{itemId:long}/link")]
    public Task<PortalContentLinkResponse> Link(long projectId, long itemId, [FromQuery] bool inline, CancellationToken cancellationToken)
        => publishing.LinkAsync(projectId, itemId, inline, cancellationToken);

    /// <summary>Adds an image item's file to the end of the draft hero images. 409 when it is one already; at most 12 (422).</summary>
    [HttpPost("heroes")]
    public Task<PortalContentResponse> AddHero(long projectId, PortalHeroRequest request, CancellationToken cancellationToken)
        => publishing.AddHeroAsync(projectId, request, cancellationToken);

    [HttpDelete("heroes/{fileId:long}")]
    public Task<PortalContentResponse> RemoveHero(long projectId, long fileId, CancellationToken cancellationToken)
        => publishing.RemoveHeroAsync(projectId, fileId, cancellationToken);

    /// <summary>The draft hero images' new order: exactly their files. 409 when the list changed meanwhile.</summary>
    [HttpPut("heroes/order")]
    public Task<PortalContentResponse> OrderHeroes(long projectId, PortalHeroOrderRequest request, CancellationToken cancellationToken)
        => publishing.OrderHeroesAsync(projectId, request, cancellationToken);

    /// <summary>The draft introduction for the client; empty clears it.</summary>
    [HttpPut("summary")]
    public Task<PortalContentResponse> SetSummary(long projectId, PortalSummaryRequest request, CancellationToken cancellationToken)
        => publishing.SetSummaryAsync(projectId, request, cancellationToken);

    /// <summary>Makes the draft what clients see, all at once. 409 when the drafts changed since the revision sent.</summary>
    [HttpPost("publish")]
    public Task<PortalContentResponse> Publish(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken)
        => publishing.PublishAsync(projectId, request, cancellationToken);

    /// <summary>Puts the client choices, hero images and introduction back to what clients see.</summary>
    [HttpPost("discard")]
    public Task<PortalContentResponse> Discard(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken)
        => publishing.DiscardAsync(projectId, request, cancellationToken);

    /// <summary>The draft, shaped exactly as the client's page.</summary>
    [HttpGet("preview")]
    public Task<PortalPresentationResponse> Preview(long projectId, CancellationToken cancellationToken)
        => publishing.PreviewAsync(projectId, cancellationToken);
}
