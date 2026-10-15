using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// A Design project's files (the Project files tab), for everyone who can open the project: who may change what, and
/// who chooses files for the client, is decided in the services. The browser sends each file in parts straight to the
/// content store through short-lived links; see docs/client-portal.md.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:long}/files")]
[HasPermission(PermissionCodes.ProjectView)]
public sealed class ProjectFilesController(IPortalContentService files, IProjectFileCommentService comments) : ControllerBase
{
    /// <summary>Every section's files, the storage they use, and what the caller may do with them.</summary>
    [HttpGet]
    public Task<ProjectFilesResponse> Get(long projectId, CancellationToken cancellationToken)
        => files.GetAsync(projectId, cancellationToken);

    /// <summary>Starts sending one file: answers with the part size and count. The same client token again gives the same upload.</summary>
    [HttpPost("uploads")]
    public async Task<ActionResult<PortalUploadStartedResponse>> StartUpload(
        long projectId, StartPortalUploadRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await files.StartUploadAsync(projectId, request, cancellationToken));

    /// <summary>Short-lived links to PUT the given parts to (up to 100 at a time).</summary>
    [HttpPost("uploads/{uploadId:long}/parts")]
    public Task<PortalPartLinksResponse> PartLinks(long projectId, long uploadId, PortalPartLinksRequest request, CancellationToken cancellationToken)
        => files.PartLinksAsync(projectId, uploadId, request, cancellationToken);

    /// <summary>Joins and checks the parts, then adds the file. Safe to repeat.</summary>
    [HttpPost("uploads/{uploadId:long}/complete")]
    public Task<ProjectFileUploadCompletedResponse> Complete(long projectId, long uploadId, CancellationToken cancellationToken)
        => files.CompleteUploadAsync(projectId, uploadId, cancellationToken);

    /// <summary>Stops an upload and drops what reached the store. Safe to repeat.</summary>
    [HttpDelete("uploads/{uploadId:long}")]
    public async Task<IActionResult> Cancel(long projectId, long uploadId, CancellationToken cancellationToken)
    {
        await files.CancelUploadAsync(projectId, uploadId, cancellationToken);
        return NoContent();
    }

    /// <summary>Renames a file, or moves it to the end of another section.</summary>
    [HttpPut("items/{itemId:long}")]
    public Task<ProjectFileResponse> Update(long projectId, long itemId, UpdatePortalItemRequest request, CancellationToken cancellationToken)
        => files.UpdateItemAsync(projectId, itemId, request, cancellationToken);

    [HttpDelete("items/{itemId:long}")]
    public Task<PortalUsageChangedResponse> Delete(long projectId, long itemId, CancellationToken cancellationToken)
        => files.DeleteItemAsync(projectId, itemId, cancellationToken);

    /// <summary>Undoes a deletion clients haven't seen published yet.</summary>
    [HttpPost("items/{itemId:long}/restore")]
    public Task<ProjectFilesResponse> Restore(long projectId, long itemId, CancellationToken cancellationToken)
        => files.RestoreAsync(projectId, itemId, cancellationToken);

    /// <summary>A short-lived link to open (images, videos, PDFs, when <paramref name="inline"/>) or download one file.</summary>
    [HttpGet("items/{itemId:long}/link")]
    public Task<PortalContentLinkResponse> Link(long projectId, long itemId, [FromQuery] bool inline, CancellationToken cancellationToken)
        => files.LinkAsync(projectId, itemId, inline, cancellationToken);

    /// <summary>A section's new order: every one of its current files, once. 409 when the section changed meanwhile.</summary>
    [HttpPut("sections/{section}/order")]
    public Task<ProjectFilesResponse> Reorder(long projectId, PortalSection section, PortalOrderRequest request, CancellationToken cancellationToken)
        => files.ReorderAsync(projectId, section, request, cancellationToken);

    /// <summary>Chooses a file for the client, or takes it back. Clients see the change once the client portal is published.</summary>
    [HttpPut("items/{itemId:long}/for-client")]
    public Task<ProjectFileResponse> SetForClient(long projectId, long itemId, ProjectFileForClientRequest request, CancellationToken cancellationToken)
        => files.SetForClientAsync(projectId, itemId, request, cancellationToken);

    /// <summary>The file's comments, oldest first.</summary>
    [HttpGet("items/{itemId:long}/comments")]
    public Task<IReadOnlyList<ProjectFileCommentResponse>> Comments(long projectId, long itemId, CancellationToken cancellationToken)
        => comments.ListAsync(projectId, itemId, cancellationToken);

    [HttpPost("items/{itemId:long}/comments")]
    public Task<ProjectFileCommentResponse> Comment(long projectId, long itemId, ProjectFileCommentRequest request, CancellationToken cancellationToken)
        => comments.AddAsync(projectId, itemId, request, cancellationToken);

    [HttpPut("comments/{commentId:long}")]
    public Task<ProjectFileCommentResponse> EditComment(long projectId, long commentId, ProjectFileCommentRequest request, CancellationToken cancellationToken)
        => comments.EditAsync(projectId, commentId, request, cancellationToken);

    [HttpDelete("comments/{commentId:long}")]
    public async Task<IActionResult> DeleteComment(long projectId, long commentId, CancellationToken cancellationToken)
    {
        await comments.DeleteAsync(projectId, commentId, cancellationToken);
        return NoContent();
    }

    /// <summary>Marks a comment fixed, or open again.</summary>
    [HttpPut("comments/{commentId:long}/fixed")]
    public Task<ProjectFileCommentResponse> SetFixed(long projectId, long commentId, ProjectFileFixedRequest request, CancellationToken cancellationToken)
        => comments.SetFixedAsync(projectId, commentId, request, cancellationToken);
}
