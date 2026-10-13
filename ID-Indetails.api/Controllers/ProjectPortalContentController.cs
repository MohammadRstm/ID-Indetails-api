using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Staff preparing a Design project's client portal files (drafts no client can see). The browser sends each file in
/// parts straight to the content store through short-lived links; see docs/client-portal.md.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:long}/portal/content")]
[HasPermission(PermissionCodes.ClientPortalManage)]
public sealed class ProjectPortalContentController(IPortalContentService portalContent, IPortalPublishingService publishing) : ControllerBase
{
    /// <summary>Every section's files, the storage they use, and whether they can change now.</summary>
    [HttpGet]
    public Task<PortalContentResponse> Get(long projectId, CancellationToken cancellationToken)
        => portalContent.GetAsync(projectId, cancellationToken);

    /// <summary>Starts sending one file: answers with the part size and count. The same client token again gives the same upload.</summary>
    [HttpPost("uploads")]
    public async Task<ActionResult<PortalUploadStartedResponse>> StartUpload(
        long projectId, StartPortalUploadRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await portalContent.StartUploadAsync(projectId, request, cancellationToken));

    /// <summary>Short-lived links to PUT the given parts to (up to 100 at a time).</summary>
    [HttpPost("uploads/{uploadId:long}/parts")]
    public Task<PortalPartLinksResponse> PartLinks(long projectId, long uploadId, PortalPartLinksRequest request, CancellationToken cancellationToken)
        => portalContent.PartLinksAsync(projectId, uploadId, request, cancellationToken);

    /// <summary>Joins and checks the parts, then adds the file. Safe to repeat.</summary>
    [HttpPost("uploads/{uploadId:long}/complete")]
    public Task<PortalUploadCompletedResponse> Complete(long projectId, long uploadId, CancellationToken cancellationToken)
        => portalContent.CompleteUploadAsync(projectId, uploadId, cancellationToken);

    /// <summary>Stops an upload and drops what reached the store. Safe to repeat.</summary>
    [HttpDelete("uploads/{uploadId:long}")]
    public async Task<IActionResult> Cancel(long projectId, long uploadId, CancellationToken cancellationToken)
    {
        await portalContent.CancelUploadAsync(projectId, uploadId, cancellationToken);
        return NoContent();
    }

    /// <summary>Renames a file, or moves it to the end of another section.</summary>
    [HttpPut("items/{itemId:long}")]
    public Task<PortalContentItemResponse> Update(long projectId, long itemId, UpdatePortalItemRequest request, CancellationToken cancellationToken)
        => portalContent.UpdateItemAsync(projectId, itemId, request, cancellationToken);

    [HttpDelete("items/{itemId:long}")]
    public Task<PortalUsageChangedResponse> Delete(long projectId, long itemId, CancellationToken cancellationToken)
        => portalContent.DeleteItemAsync(projectId, itemId, cancellationToken);

    /// <summary>A short-lived link to open (images, videos, PDFs, when <paramref name="inline"/>) or download one file.</summary>
    [HttpGet("items/{itemId:long}/link")]
    public Task<PortalContentLinkResponse> Link(long projectId, long itemId, [FromQuery] bool inline, CancellationToken cancellationToken)
        => portalContent.LinkAsync(projectId, itemId, inline, cancellationToken);

    /// <summary>A section's new order: every one of its current files, once. 409 when the section changed meanwhile.</summary>
    [HttpPut("sections/{section}/order")]
    public Task<PortalContentResponse> Reorder(long projectId, PortalSection section, PortalOrderRequest request, CancellationToken cancellationToken)
        => publishing.ReorderAsync(projectId, section, request, cancellationToken);

    /// <summary>Uses an image item's file as the draft hero.</summary>
    [HttpPut("hero")]
    public Task<PortalContentResponse> SetHero(long projectId, PortalHeroRequest request, CancellationToken cancellationToken)
        => publishing.SetHeroAsync(projectId, request, cancellationToken);

    [HttpDelete("hero")]
    public Task<PortalContentResponse> ClearHero(long projectId, CancellationToken cancellationToken)
        => publishing.ClearHeroAsync(projectId, cancellationToken);

    /// <summary>The draft introduction for the client; empty clears it.</summary>
    [HttpPut("summary")]
    public Task<PortalContentResponse> SetSummary(long projectId, PortalSummaryRequest request, CancellationToken cancellationToken)
        => publishing.SetSummaryAsync(projectId, request, cancellationToken);

    /// <summary>Undoes a removal before it is published.</summary>
    [HttpPost("items/{itemId:long}/restore")]
    public Task<PortalContentResponse> Restore(long projectId, long itemId, CancellationToken cancellationToken)
        => publishing.RestoreAsync(projectId, itemId, cancellationToken);

    /// <summary>Makes the draft what clients see, all at once. 409 when the drafts changed since the revision sent.</summary>
    [HttpPost("publish")]
    public Task<PortalContentResponse> Publish(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken)
        => publishing.PublishAsync(projectId, request, cancellationToken);

    /// <summary>Puts the draft back to what clients see; files never published are deleted.</summary>
    [HttpPost("discard")]
    public Task<PortalContentResponse> Discard(long projectId, PortalRevisionRequest request, CancellationToken cancellationToken)
        => publishing.DiscardAsync(projectId, request, cancellationToken);

    /// <summary>The draft, shaped exactly as the client's page.</summary>
    [HttpGet("preview")]
    public Task<PortalPresentationResponse> Preview(long projectId, CancellationToken cancellationToken)
        => publishing.PreviewAsync(projectId, cancellationToken);
}
