using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Portal.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// The client's side of a project's discussion (client portal token only). Clients with live access to the Design
/// project take part; anything else answers 404. Their writes have a per-account limit.
/// </summary>
[ApiController]
[Route("api/portal/projects/{projectId:long}/discussion")]
[Authorize(Policy = AuthorizationPolicies.Portal)]
public sealed class PortalDiscussionController(IPortalDiscussionService discussion) : ControllerBase
{
    private const DiscussionSide Side = DiscussionSide.Client;

    [HttpGet]
    public Task<DiscussionSummaryResponse> Summary(long projectId, CancellationToken cancellationToken)
        => discussion.SummaryAsync(Side, projectId, cancellationToken);

    [HttpGet("messages")]
    public Task<PortalMessagePageResponse> Messages(long projectId, [FromQuery] long? before, [FromQuery] int? limit, CancellationToken cancellationToken)
        => discussion.MessagesAsync(Side, projectId, before, limit, cancellationToken);

    [HttpPost("messages")]
    [EnableRateLimiting(RateLimitPolicies.PortalWrite)]
    public Task<PortalChatMessageResponse> Send(long projectId, PortalMessageRequest request, CancellationToken cancellationToken)
        => discussion.SendMessageAsync(Side, projectId, request, cancellationToken);

    [HttpPut("messages/{messageId:long}")]
    [EnableRateLimiting(RateLimitPolicies.PortalWrite)]
    public Task<PortalChatMessageResponse> EditMessage(long projectId, long messageId, PortalEditRequest request, CancellationToken cancellationToken)
        => discussion.EditMessageAsync(Side, projectId, messageId, request, cancellationToken);

    [HttpGet("comments/{itemId:long}")]
    public Task<PortalCommentThreadResponse> Thread(long projectId, long itemId, CancellationToken cancellationToken)
        => discussion.ThreadAsync(Side, projectId, itemId, cancellationToken);

    [HttpPost("comments")]
    [EnableRateLimiting(RateLimitPolicies.PortalWrite)]
    public Task<PortalCommentResponse> Comment(long projectId, PortalCommentRequest request, CancellationToken cancellationToken)
        => discussion.AddCommentAsync(Side, projectId, request, cancellationToken);

    [HttpPut("comments/{commentId:long}")]
    [EnableRateLimiting(RateLimitPolicies.PortalWrite)]
    public Task<PortalCommentResponse> EditComment(long projectId, long commentId, PortalEditRequest request, CancellationToken cancellationToken)
        => discussion.EditCommentAsync(Side, projectId, commentId, request, cancellationToken);

    [HttpDelete("comments/{commentId:long}")]
    [EnableRateLimiting(RateLimitPolicies.PortalWrite)]
    public Task<PortalCommentResponse> DeleteComment(long projectId, long commentId, CancellationToken cancellationToken)
        => discussion.DeleteCommentAsync(Side, projectId, commentId, cancellationToken);

    [HttpPost("read")]
    public Task<DiscussionSummaryResponse> Read(long projectId, PortalReadRequest request, CancellationToken cancellationToken)
        => discussion.MarkReadAsync(Side, projectId, request, cancellationToken);
}
