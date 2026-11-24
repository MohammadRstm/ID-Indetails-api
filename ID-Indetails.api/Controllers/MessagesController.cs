using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Files;
using ID_Indetails.api.DTO.Request.Messaging;
using ID_Indetails.api.DTO.Response.Messaging;
using ID_Indetails.api.Services.Messaging.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>
/// Team chats, direct messages, Announcements and custom groups. No permission needed to read: who may read and write each chat
/// follows role and team (see <c>Business/Messaging/MessagingRules</c>). Every active person reads Announcements;
/// writing in it needs <c>message:announce</c>. Custom groups are read and written by their members, and created and
/// changed by holders of <c>message:manage_groups</c>.
/// </summary>
[ApiController]
[Route("api/messages")]
[Authorize]
public sealed class MessagesController(IMessagingService messagingService) : ControllerBase
{
    [HttpGet("conversations")]
    public Task<IReadOnlyList<ConversationResponse>> Conversations(CancellationToken cancellationToken)
        => messagingService.ConversationsAsync(cancellationToken);

    /// <summary>Loading the badge means the caller's device has their messages: it also moves their received markers.</summary>
    [HttpGet("badge")]
    public Task<MessagesBadgeResponse> Badge(CancellationToken cancellationToken)
        => messagingService.ReceiveAsync(cancellationToken);

    [HttpGet("{messageId:long}/receipts")]
    public Task<MessageReceiptsResponse> Receipts(long messageId, CancellationToken cancellationToken)
        => messagingService.ReceiptsAsync(messageId, cancellationToken);

    /// <summary>Only the sender deletes, and only a voice message (<c>Business/Messaging/MessagingRules.CheckDelete</c>).</summary>
    [HttpDelete("{messageId:long}")]
    public async Task<IActionResult> Delete(long messageId, CancellationToken cancellationToken)
    {
        await messagingService.DeleteAsync(messageId, cancellationToken);
        return NoContent();
    }

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

    [HttpGet("announcements")]
    public Task<MessagePageResponse> Announcements([FromQuery] MessagePageQuery query, CancellationToken cancellationToken)
        => messagingService.AnnouncementsAsync(query, cancellationToken);

    [HttpPost("announcements")]
    [HasPermission(PermissionCodes.MessageAnnounce)]
    public async Task<ActionResult<MessageResponse>> SendAnnouncement(SendMessageRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await messagingService.AnnounceAsync(request, cancellationToken));

    [HttpPost("announcements/read")]
    public Task<MessagesBadgeResponse> MarkAnnouncementsRead(CancellationToken cancellationToken)
        => messagingService.MarkAnnouncementsReadAsync(cancellationToken);

    /// <summary>Every custom group, with its members and whether the caller is one of them, for managing them.</summary>
    [HttpGet("groups")]
    [HasPermission(PermissionCodes.MessageManageGroups)]
    public Task<IReadOnlyList<ChatGroupDetailResponse>> Groups(CancellationToken cancellationToken)
        => messagingService.GroupsAsync(cancellationToken);

    /// <summary>Every active member of staff, of any business: who can be put in a group.</summary>
    [HttpGet("groups/people")]
    [HasPermission(PermissionCodes.MessageManageGroups)]
    public Task<IReadOnlyList<ContactResponse>> GroupCandidates(CancellationToken cancellationToken)
        => messagingService.GroupCandidatesAsync(cancellationToken);

    [HttpPost("groups")]
    [HasPermission(PermissionCodes.MessageManageGroups)]
    public async Task<ActionResult<ChatGroupDetailResponse>> CreateGroup(CreateChatGroupRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await messagingService.CreateGroupAsync(request, cancellationToken));

    [HttpPut("groups/{groupId:long}")]
    [HasPermission(PermissionCodes.MessageManageGroups)]
    public Task<ChatGroupDetailResponse> RenameGroup(long groupId, RenameChatGroupRequest request, CancellationToken cancellationToken)
        => messagingService.RenameGroupAsync(groupId, request, cancellationToken);

    [HttpDelete("groups/{groupId:long}")]
    [HasPermission(PermissionCodes.MessageManageGroups)]
    public async Task<IActionResult> DeleteGroup(long groupId, CancellationToken cancellationToken)
    {
        await messagingService.DeleteGroupAsync(groupId, cancellationToken);
        return NoContent();
    }

    [HttpPost("groups/{groupId:long}/members")]
    [HasPermission(PermissionCodes.MessageManageGroups)]
    public Task<ChatGroupDetailResponse> AddGroupMembers(long groupId, AddChatGroupMembersRequest request, CancellationToken cancellationToken)
        => messagingService.AddGroupMembersAsync(groupId, request, cancellationToken);

    [HttpDelete("groups/{groupId:long}/members/{userId:long}")]
    [HasPermission(PermissionCodes.MessageManageGroups)]
    public Task<ChatGroupDetailResponse> RemoveGroupMember(long groupId, long userId, CancellationToken cancellationToken)
        => messagingService.RemoveGroupMemberAsync(groupId, userId, cancellationToken);

    [HttpPut("groups/{groupId:long}/photo")]
    [HasPermission(PermissionCodes.MessageManageGroups)]
    public Task<ChatGroupDetailResponse> SetGroupPhoto(long groupId, AttachFileRequest request, CancellationToken cancellationToken)
        => messagingService.SetGroupPhotoAsync(groupId, request, cancellationToken);

    [HttpDelete("groups/{groupId:long}/photo")]
    [HasPermission(PermissionCodes.MessageManageGroups)]
    public Task<ChatGroupDetailResponse> RemoveGroupPhoto(long groupId, CancellationToken cancellationToken)
        => messagingService.RemoveGroupPhotoAsync(groupId, cancellationToken);

    /// <summary>A member leaves the group; anyone else reads it as not found.</summary>
    [HttpPost("groups/{groupId:long}/leave")]
    public async Task<IActionResult> LeaveGroup(long groupId, CancellationToken cancellationToken)
    {
        await messagingService.LeaveGroupAsync(groupId, cancellationToken);
        return NoContent();
    }

    [HttpGet("groups/{groupId:long}/members")]
    public Task<IReadOnlyList<ContactResponse>> GroupMembers(long groupId, CancellationToken cancellationToken)
        => messagingService.GroupMembersAsync(groupId, cancellationToken);

    [HttpGet("groups/{groupId:long}")]
    public Task<MessagePageResponse> GroupMessages(long groupId, [FromQuery] MessagePageQuery query, CancellationToken cancellationToken)
        => messagingService.GroupMessagesAsync(groupId, query, cancellationToken);

    [HttpPost("groups/{groupId:long}")]
    public async Task<ActionResult<MessageResponse>> SendToGroup(long groupId, SendMessageRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await messagingService.SendToGroupAsync(groupId, request, cancellationToken));

    [HttpPost("groups/{groupId:long}/read")]
    public Task<MessagesBadgeResponse> MarkGroupRead(long groupId, CancellationToken cancellationToken)
        => messagingService.MarkGroupReadAsync(groupId, cancellationToken);
}
