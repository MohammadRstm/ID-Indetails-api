using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
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
public sealed class ProjectPortalContentController(IPortalContentService portalContent) : ControllerBase
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
}
