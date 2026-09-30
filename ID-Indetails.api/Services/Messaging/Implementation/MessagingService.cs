using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Messaging;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Messaging;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Messaging;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Messaging.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Files.Abstraction;
using ID_Indetails.api.Services.Files.Implementation;
using ID_Indetails.api.Services.Messaging.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.api.Services.Messaging.Implementation;

/// <summary>
/// Chats are addressed by team or by person, never by an internal id. A conversation row is created with its first
/// message. A team chat the caller isn't in, or a person they can't reach, reads as not found. Sending marks the chat
/// read for the sender. Messages can't be edited or deleted.
/// </summary>
public sealed class MessagingService(
    ICurrentUser currentUser,
    IMessagingRepository messaging,
    IWorkEntryRepository entries,
    IFileClaims fileClaims,
    IUnitOfWork unitOfWork,
    ILiveUpdates live,
    IClock clock) : IMessagingService
{
    private const string ChatNotFound = "Chat not found.";
    private const string PersonNotFound = "Person not found.";

    /// <summary>The caller's team chats, the states of the conversations they may still read, and the people in their direct chats.</summary>
    private sealed record Inbox(IReadOnlyList<ChatTeam> Teams, IReadOnlyList<ConversationState> States, IReadOnlyDictionary<long, ChatPerson> People);

    public async Task<IReadOnlyList<ConversationResponse>> ConversationsAsync(CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var (teams, states, people) = await InboxAsync(me, cancellationToken);
        var lastIds = states.Where(s => s.LastMessageId is not null).Select(s => s.LastMessageId!.Value).ToList();
        var last = (await DescribeAsync(await messaging.MessagesByIdAsync(lastIds, cancellationToken), cancellationToken))
            .ToDictionary(m => m.Id);
        MessageResponse? LastOf(ConversationState? s) => s?.LastMessageId is { } id ? last.GetValueOrDefault(id) : null;

        var byTeam = states.Where(s => s.TeamId is not null).ToDictionary(s => s.TeamId!.Value);
        var teamChats = teams.Select(t =>
        {
            var state = byTeam.GetValueOrDefault(t.Id);
            return new ConversationResponse(
                ConversationType.Group, new ChatTeamResponse(t.Id, t.Name, t.Domain), null, LastOf(state), state?.Unread ?? 0, state?.UnreadMention ?? false);
        });
        var directChats = states
            .Where(s => s.OtherUserId is { } other && people.ContainsKey(other))
            .Select(s => new ConversationResponse(
                ConversationType.Dm, null, ToContact(people[s.OtherUserId!.Value]), LastOf(s), s.Unread, false));

        return teamChats.Concat(directChats)
            .OrderByDescending(c => c.LastMessage?.CreatedAt ?? DateTime.MinValue)
            .ThenBy(c => c.Team?.Name ?? c.Person?.Name)
            .ToList();
    }

    public async Task<MessagesBadgeResponse> BadgeAsync(CancellationToken cancellationToken = default)
    {
        var inbox = await InboxAsync(await ViewerAsync(cancellationToken), cancellationToken);
        return new MessagesBadgeResponse(inbox.States.Sum(s => s.Unread), inbox.States.Any(s => s.UnreadMention));
    }

    public async Task<IReadOnlyList<ContactResponse>> ContactsAsync(CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        return (await messaging.ActivePeopleAsync(cancellationToken))
            .Where(p => MessagingRules.CanReach(Rule(me), Rule(p)))
            .Select(ToContact)
            .ToList();
    }

    public async Task<IReadOnlyList<ContactResponse>> TeamMembersAsync(long teamId, CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var team = await TeamChatAsync(me, teamId, cancellationToken);
        return (await MembersAsync(team, cancellationToken)).Select(ToContact).ToList();
    }

    public async Task<MessagePageResponse> TeamMessagesAsync(long teamId, MessagePageQuery query, CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var team = await TeamChatAsync(me, teamId, cancellationToken);
        return await PageAsync(await messaging.TeamConversationAsync(team.Id, cancellationToken), query, cancellationToken);
    }

    public async Task<MessageResponse> SendToTeamAsync(long teamId, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var team = await TeamChatAsync(me, teamId, cancellationToken);
        var mentionIds = request.MentionIds.Distinct().ToList();
        var memberIds = (await MembersAsync(team, cancellationToken)).Select(p => p.Id).ToHashSet();
        MessagingRules.CheckMentions(mentionIds, memberIds).ThrowIfDenied();

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var conversation = await TeamConversationForWriteAsync(team, cancellationToken);
        var message = await PostAsync(
            conversation, me.Id, request.Body, MessageKinds.Text, mentionIds, request.AttachmentIds, null, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        TeamChanged(team, memberIds);

        return await OneAsync(message.Id, cancellationToken);
    }

    public async Task<MessagesBadgeResponse> MarkTeamReadAsync(long teamId, CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var team = await TeamChatAsync(me, teamId, cancellationToken);
        await MarkReadAsync(me, await messaging.TeamConversationAsync(team.Id, cancellationToken), cancellationToken);
        live.ChatChanged(me.Id, new LiveChat(team.Id, null));
        return await BadgeAsync(cancellationToken);
    }

    public async Task<MessagePageResponse> DirectMessagesAsync(long userId, MessagePageQuery query, CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var other = await ReachableAsync(me, userId, cancellationToken);
        return await PageAsync(await messaging.DirectConversationAsync(me.Id, other.Id, cancellationToken), query, cancellationToken);
    }

    public async Task<MessageResponse> SendDirectAsync(long userId, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var other = await ReachableAsync(me, userId, cancellationToken);
        MessagingRules.CheckMentions(request.MentionIds, null).ThrowIfDenied();
        MessagingRules.CheckRecipient(Rule(other)).ThrowIfDenied();

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var conversation = await messaging.DirectConversationForUpdateAsync(me.Id, other.Id, cancellationToken);
        if (conversation is null)
        {
            conversation = new Conversation
            {
                Type = ConversationType.Dm,
                DmUserLowId = Math.Min(me.Id, other.Id),
                DmUserHighId = Math.Max(me.Id, other.Id),
                CreatedAt = clock.UtcNow,
            };
            messaging.Add(conversation);
        }

        var message = await PostAsync(conversation, me.Id, request.Body, MessageKinds.Text, [], request.AttachmentIds, null, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        live.ChatChanged(other.Id, new LiveChat(null, me.Id));
        live.ChatChanged(me.Id, new LiveChat(null, other.Id));

        return await OneAsync(message.Id, cancellationToken);
    }

    public async Task<MessagesBadgeResponse> MarkDirectReadAsync(long userId, CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var other = await ReachableAsync(me, userId, cancellationToken);
        await MarkReadAsync(me, await messaging.DirectConversationAsync(me.Id, other.Id, cancellationToken), cancellationToken);
        live.ChatChanged(me.Id, new LiveChat(null, other.Id));
        return await BadgeAsync(cancellationToken);
    }

    public async Task<bool> CanReadConversationAsync(long conversationId, CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var conversation = await messaging.ConversationAsync(conversationId, cancellationToken);
        if (conversation?.TeamId is { } teamId)
        {
            return (await VisibleTeamsAsync(me, cancellationToken)).Any(t => t.Id == teamId);
        }

        if (conversation is null || (conversation.DmUserLowId != me.Id && conversation.DmUserHighId != me.Id))
        {
            return false;
        }

        var otherId = conversation.DmUserLowId == me.Id ? conversation.DmUserHighId!.Value : conversation.DmUserLowId!.Value;
        var other = (await messaging.PeopleAsync([otherId], cancellationToken)).SingleOrDefault();
        return other is not null && MessagingRules.CanReach(Rule(me), Rule(other));
    }

    public async Task<long> PostTaskCommentAsync(
        long commenterId,
        long entryAuthorId,
        string entryDomain,
        long entryId,
        long commentId,
        string body,
        CancellationToken cancellationToken = default)
    {
        var people = (await messaging.PeopleAsync([commenterId, entryAuthorId], cancellationToken)).ToDictionary(p => p.Id);
        var author = people[entryAuthorId];
        var team = author.TeamId is { } teamId && author.Domain == entryDomain
            ? (await messaging.TeamsAsync(cancellationToken)).SingleOrDefault(t => t.Id == teamId)
            : null;
        if (team is null)
        {
            throw new BusinessRuleException(ErrorCodes.NoTeamChat, "The entry's author isn't in a team, so there's no team chat to post to.");
        }

        if (!MessagingRules.IsMember(Rule(people[commenterId]), Chat(team)))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You aren't in that team's chat.");
        }

        // The card speaks to the whole chat (the prototype's @all).
        var memberIds = (await MembersAsync(team, cancellationToken)).Select(p => p.Id).ToList();
        var conversation = await TeamConversationForWriteAsync(team, cancellationToken);
        var message = await PostAsync(
            conversation, commenterId, body, MessageKinds.TaskComment, memberIds.Where(id => id != commenterId).ToList(), [], entryId, commentId, cancellationToken);
        TeamChanged(team, memberIds);
        return message.Id;
    }

    /// <summary>The signed-in person, who must still be active: an access token outlives a deactivation.</summary>
    private async Task<ChatPerson> ViewerAsync(CancellationToken cancellationToken)
    {
        var me = (await messaging.PeopleAsync([currentUser.Id], cancellationToken)).SingleOrDefault();
        return me is { Status: UserStatus.Active } ? me : throw new ForbiddenException(ErrorCodes.Forbidden, "You don't have permission to do this.");
    }

    private async Task<IReadOnlyList<ChatTeam>> VisibleTeamsAsync(ChatPerson me, CancellationToken cancellationToken)
        => (await messaging.TeamsAsync(cancellationToken)).Where(t => MessagingRules.IsMember(Rule(me), Chat(t))).ToList();

    private async Task<ChatTeam> TeamChatAsync(ChatPerson me, long teamId, CancellationToken cancellationToken)
        => (await VisibleTeamsAsync(me, cancellationToken)).SingleOrDefault(t => t.Id == teamId) ?? throw new NotFoundException(ChatNotFound);

    private async Task<ChatPerson> ReachableAsync(ChatPerson me, long userId, CancellationToken cancellationToken)
    {
        var other = (await messaging.PeopleAsync([userId], cancellationToken)).SingleOrDefault();
        return other is not null && MessagingRules.CanReach(Rule(me), Rule(other)) ? other : throw new NotFoundException(PersonNotFound);
    }

    private async Task<IReadOnlyList<ChatPerson>> MembersAsync(ChatTeam team, CancellationToken cancellationToken)
        => (await messaging.ActivePeopleAsync(cancellationToken)).Where(p => MessagingRules.IsMember(Rule(p), Chat(team))).ToList();

    /// <summary>Only chats the caller is still in, and people they can still reach.</summary>
    private async Task<Inbox> InboxAsync(ChatPerson me, CancellationToken cancellationToken)
    {
        var teams = await VisibleTeamsAsync(me, cancellationToken);
        var states = await messaging.StatesAsync(me.Id, teams.Select(t => t.Id).ToList(), cancellationToken);
        var others = states.Where(s => s.OtherUserId is not null).Select(s => s.OtherUserId!.Value).ToList();
        var people = (await messaging.PeopleAsync(others, cancellationToken))
            .Where(p => MessagingRules.CanReach(Rule(me), Rule(p)))
            .ToDictionary(p => p.Id);

        return new Inbox(teams, states.Where(s => s.OtherUserId is null || people.ContainsKey(s.OtherUserId.Value)).ToList(), people);
    }

    /// <summary>The team's chat for a new message, created with the first one.</summary>
    private async Task<Conversation> TeamConversationForWriteAsync(ChatTeam team, CancellationToken cancellationToken)
    {
        var conversation = await messaging.TeamConversationForUpdateAsync(team.Id, cancellationToken);
        if (conversation is null)
        {
            conversation = new Conversation { Type = ConversationType.Group, DomainCode = team.Domain, TeamId = team.Id, CreatedAt = clock.UtcNow };
            messaging.Add(conversation);
        }

        return conversation;
    }

    /// <summary>Saves the message with the uploads it takes, and moves the sender's read marker to it.</summary>
    private async Task<Message> PostAsync(
        Conversation conversation,
        long senderId,
        string body,
        string kind,
        IReadOnlyCollection<long> mentionIds,
        IReadOnlyCollection<long> attachmentIds,
        long? entryId,
        long? commentId,
        CancellationToken cancellationToken)
    {
        var attachments = new List<MessageAttachment>();
        foreach (var fileId in attachmentIds)
        {
            attachments.Add(new MessageAttachment { FileId = (await fileClaims.ClaimAsync(fileId, null, cancellationToken)).Id });
        }

        var message = new Message
        {
            Conversation = conversation,
            SenderId = senderId,
            Body = string.IsNullOrWhiteSpace(body) ? null : body.Trim(),
            Kind = kind,
            RefEntryId = entryId,
            RefCommentId = commentId,
            CreatedAt = clock.UtcNow,
            Mentions = mentionIds.Select(id => new MessageMention { UserId = id }).ToList(),
            Attachments = attachments,
        };
        messaging.Add(message);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await MoveReadMarkerAsync(senderId, conversation.Id, message.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return message;
    }

    private async Task MarkReadAsync(ChatPerson me, Conversation? conversation, CancellationToken cancellationToken)
    {
        if (conversation is null || await messaging.LastMessageIdAsync(conversation.Id, cancellationToken) is not { } lastId)
        {
            return;
        }

        await MoveReadMarkerAsync(me.Id, conversation.Id, lastId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Read markers only move forward.</summary>
    private async Task MoveReadMarkerAsync(long userId, long conversationId, long messageId, CancellationToken cancellationToken)
    {
        var read = await messaging.ReadForUpdateAsync(userId, conversationId, cancellationToken);
        if (read is null)
        {
            messaging.Add(new ConversationRead { UserId = userId, ConversationId = conversationId, LastReadMessageId = messageId, UpdatedAt = clock.UtcNow });
        }
        else if ((read.LastReadMessageId ?? 0) < messageId)
        {
            read.LastReadMessageId = messageId;
            read.UpdatedAt = clock.UtcNow;
        }
    }

    private async Task<MessagePageResponse> PageAsync(Conversation? conversation, MessagePageQuery query, CancellationToken cancellationToken)
    {
        if (conversation is null)
        {
            return new MessagePageResponse([], false);
        }

        var newestFirst = await messaging.MessagesAsync(conversation.Id, query.Before, query.Limit + 1, cancellationToken);
        var page = newestFirst.Take(query.Limit).Reverse().ToList();
        return new MessagePageResponse(await DescribeAsync(page, cancellationToken), newestFirst.Count > query.Limit);
    }

    private async Task<MessageResponse> OneAsync(long messageId, CancellationToken cancellationToken)
        => (await DescribeAsync(await messaging.MessagesByIdAsync([messageId], cancellationToken), cancellationToken)).Single();

    /// <summary>Keeps the order given, and adds the entry behind each shared comment.</summary>
    private async Task<IReadOnlyList<MessageResponse>> DescribeAsync(IReadOnlyList<MessageView> messages, CancellationToken cancellationToken)
    {
        var entryIds = messages.Where(m => m.RefEntryId is not null).Select(m => m.RefEntryId!.Value).Distinct().ToList();
        var shared = entryIds.Count > 0 ? (await entries.ViewsByIdAsync(entryIds, cancellationToken)).ToDictionary(e => e.Id) : [];

        return messages.Select(m => new MessageResponse(
                m.Id,
                new ChatPersonResponse(m.Sender.Id, m.Sender.Name, m.Sender.PhotoFileId),
                m.Body,
                m.Kind,
                m.Mentions.Select(p => new ChatPersonResponse(p.Id, p.Name)).ToList(),
                m.Attachments.Select(FileMapper.ToResponse).ToList(),
                m.RefEntryId is { } entryId && shared.TryGetValue(entryId, out var e)
                    ? new TaskCommentResponse(
                        e.Id, m.RefCommentId ?? 0, new ChatPersonResponse(e.UserId, e.UserName), e.ProjectId, e.ProjectTitle, e.Description, e.StartedAt, e.Minutes,
                        e.Attachment is null ? null : FileMapper.ToResponse(e.Attachment))
                    : null,
                m.CreatedAt))
            .ToList();
    }

    private void TeamChanged(ChatTeam team, IEnumerable<long> memberIds)
    {
        foreach (var id in memberIds)
        {
            live.ChatChanged(id, new LiveChat(team.Id, null));
        }
    }

    private static MessagingRules.Person Rule(ChatPerson p) => new(p.Id, p.Role, p.Domain, p.TeamId, p.Status);

    private static MessagingRules.TeamChat Chat(ChatTeam t) => new(t.Id, t.Domain);

    private static ContactResponse ToContact(ChatPerson p) => new(
        p.Id, p.Name, p.Role, p.Domain, p.TeamId is { } id ? new TeamSummaryResponse(id, p.TeamName ?? string.Empty) : null, p.Position, p.PhotoFileId);
}
