using ID_Indetails.api.DTO.Request.Messaging;
using ID_Indetails.api.DTO.Response.Messaging;
using ID_Indetails.api.Services.Messaging.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Team chats and direct messages. No permission needed: who may read and write each chat follows role and team
/// (see <c>Business/Messaging/MessagingRules</c>).
/// </summary>
[ApiController]
[Route("api/messages")]
[Authorize]
public sealed class MessagesController(IMessagingService messagingService) : ControllerBase
{
    [HttpGet("conversations")]
    public Task<IReadOnlyList<ConversationResponse>> Conversations(CancellationToken cancellationToken)
        => messagingService.ConversationsAsync(cancellationToken);

    [HttpGet("badge")]
    public Task<MessagesBadgeResponse> Badge(CancellationToken cancellationToken)
        => messagingService.BadgeAsync(cancellationToken);

    [HttpGet("contacts")]
    public Task<IReadOnlyList<ContactResponse>> Contacts(CancellationToken cancellationToken)
        => messagingService.ContactsAsync(cancellationToken);

    [HttpGet("teams/{teamId:long}/members")]
    public Task<IReadOnlyList<ContactResponse>> TeamMembers(long teamId, CancellationToken cancellationToken)
        => messagingService.TeamMembersAsync(teamId, cancellationToken);

    [HttpGet("teams/{teamId:long}")]
    public Task<MessagePageResponse> TeamMessages(long teamId, [FromQuery] MessagePageQuery query, CancellationToken cancellationToken)
        => messagingService.TeamMessagesAsync(teamId, query, cancellationToken);

    [HttpPost("teams/{teamId:long}")]
    public async Task<ActionResult<MessageResponse>> SendToTeam(long teamId, SendMessageRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await messagingService.SendToTeamAsync(teamId, request, cancellationToken));

    [HttpPost("teams/{teamId:long}/read")]
    public Task<MessagesBadgeResponse> MarkTeamRead(long teamId, CancellationToken cancellationToken)
        => messagingService.MarkTeamReadAsync(teamId, cancellationToken);

    [HttpGet("direct/{userId:long}")]
    public Task<MessagePageResponse> DirectMessages(long userId, [FromQuery] MessagePageQuery query, CancellationToken cancellationToken)
        => messagingService.DirectMessagesAsync(userId, query, cancellationToken);

    [HttpPost("direct/{userId:long}")]
    public async Task<ActionResult<MessageResponse>> SendDirect(long userId, SendMessageRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await messagingService.SendDirectAsync(userId, request, cancellationToken));

    [HttpPost("direct/{userId:long}/read")]
    public Task<MessagesBadgeResponse> MarkDirectRead(long userId, CancellationToken cancellationToken)
        => messagingService.MarkDirectReadAsync(userId, cancellationToken);
}
