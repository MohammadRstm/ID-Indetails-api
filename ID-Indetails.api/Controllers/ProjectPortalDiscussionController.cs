using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// The staff side of a Design client project's discussion: the project chat and comments on published files. Only
/// staff taking part (super admins; the project's manager, whoever uploaded a file chosen for the client or logged work
/// on it, while they can see Design) get past the 404; see docs/client-portal.md.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:long}/portal/discussion")]
[Authorize]
public sealed class ProjectPortalDiscussionController(IPortalDiscussionService discussion) : ControllerBase
{
    private const DiscussionSide Side = DiscussionSide.Staff;

    [HttpGet]
    public Task<DiscussionSummaryResponse> Summary(long projectId, CancellationToken cancellationToken)
        => discussion.SummaryAsync(Side, projectId, cancellationToken);

    [HttpGet("messages")]
    public Task<PortalMessagePageResponse> Messages(long projectId, [FromQuery] long? before, [FromQuery] int? limit, CancellationToken cancellationToken)
        => discussion.MessagesAsync(Side, projectId, before, limit, cancellationToken);

    [HttpPost("messages")]
    public Task<PortalChatMessageResponse> Send(long projectId, PortalMessageRequest request, CancellationToken cancellationToken)
        => discussion.SendMessageAsync(Side, projectId, request, cancellationToken);

    [HttpPut("messages/{messageId:long}")]
    public Task<PortalChatMessageResponse> EditMessage(long projectId, long messageId, PortalEditRequest request, CancellationToken cancellationToken)
        => discussion.EditMessageAsync(Side, projectId, messageId, request, cancellationToken);

    [HttpGet("comments/{itemId:long}")]
    public Task<PortalCommentThreadResponse> Thread(long projectId, long itemId, CancellationToken cancellationToken)
        => discussion.ThreadAsync(Side, projectId, itemId, cancellationToken);

    [HttpPost("comments")]
    public Task<PortalCommentResponse> Comment(long projectId, PortalCommentRequest request, CancellationToken cancellationToken)
        => discussion.AddCommentAsync(Side, projectId, request, cancellationToken);

    [HttpPut("comments/{commentId:long}")]
    public Task<PortalCommentResponse> EditComment(long projectId, long commentId, PortalEditRequest request, CancellationToken cancellationToken)
        => discussion.EditCommentAsync(Side, projectId, commentId, request, cancellationToken);

    [HttpDelete("comments/{commentId:long}")]
    public Task<PortalCommentResponse> DeleteComment(long projectId, long commentId, CancellationToken cancellationToken)
        => discussion.DeleteCommentAsync(Side, projectId, commentId, cancellationToken);

    [HttpPost("read")]
    public Task<DiscussionSummaryResponse> Read(long projectId, PortalReadRequest request, CancellationToken cancellationToken)
        => discussion.MarkReadAsync(Side, projectId, request, cancellationToken);
}
