using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Messaging;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Messaging;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Messaging;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Files.Abstraction;
using ID_Indetails.api.Repositories.Messaging.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Files.Abstraction;
using ID_Indetails.api.Services.Files.Implementation;
using ID_Indetails.api.Services.Messaging.Abstraction;
using ID_Indetails.api.Services.Push.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Implementation;

namespace ID_Indetails.api.Services.Messaging.Implementation;

/// <summary>
/// Chats are addressed by team or by person, never by an internal id. A conversation row is created with its first
/// message; Announcements, the one chat everyone reads, was created with the migration that added it. A team chat the
/// caller isn't in, or a person they can't reach, reads as not found. Sending marks the chat read for the sender.
/// Messages can't be edited; whoever sent one can delete it, with its files. Their ticks and "Seen by" come from the read markers (<see cref="MessageReceipts"/>).
/// </summary>
public sealed class MessagingService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IMessagingRepository messaging,
    IWorkEntryRepository entries,
    IFileClaims fileClaims,
    IFileRepository files,
    IFileStorage storage,
    IUnitOfWork unitOfWork,
    ILiveUpdates live,
    IPushQueue pushes,
    IPresence presence,
    IClock clock) : IMessagingService
{
    private const string ChatNotFound = "Chat not found.";
    private const string PersonNotFound = "Person not found.";
    private const string MessageNotFound = "Message not found.";

    /// <summary>The caller's team chats, the states of the conversations they may still read, and the people in their direct chats.</summary>
    private sealed record Inbox(IReadOnlyList<ChatTeam> Teams, IReadOnlyList<ConversationState> States, IReadOnlyDictionary<long, ChatPerson> People);

    /// <summary>A conversation the caller may read: a team's chat (with the team), a direct chat (with the other person), or Announcements.</summary>
    private sealed record ChatContext(long ConversationId, ConversationType Type, ChatTeam? Team, ChatPerson? Other);

    public async Task<IReadOnlyList<ConversationResponse>> ConversationsAsync(CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var (teams, states, people) = await InboxAsync(me, cancellationToken);
        var active = await messaging.ActivePeopleAsync(cancellationToken);
        var teamById = teams.ToDictionary(t => t.Id);
        var chats = states.ToDictionary(s => s.Id, s => new ChatContext(
            s.Id, s.Type, s.TeamId is { } teamId ? teamById.GetValueOrDefault(teamId) : null, s.OtherUserId is { } other ? people.GetValueOrDefault(other) : null));
        var lastIds = states.Where(s => s.LastMessageId is not null).Select(s => s.LastMessageId!.Value).ToList();
        var lastViews = await messaging.MessagesByIdAsync(lastIds, cancellationToken);
        var receipts = await ReceiptsAsync(me, lastViews.Select(m => (chats[m.ConversationId], m)).ToList(), active, cancellationToken);
        var last = (await DescribeAsync(lastViews, receipts, cancellationToken)).ToDictionary(m => m.Id);
        MessageResponse? LastOf(ConversationState? s) => s?.LastMessageId is { } id ? last.GetValueOrDefault(id) : null;

        var byTeam = states.Where(s => s.TeamId is not null).ToDictionary(s => s.TeamId!.Value);
        var teamChats = teams.Select(t =>
        {
            var state = byTeam.GetValueOrDefault(t.Id);
            var online = active.Count(p => p.Id != me.Id && presence.IsOnline(p.Id) && MessagingRules.IsMember(Rule(p), Chat(t)));
            return new ConversationResponse(
                ConversationType.Group, new ChatTeamResponse(t.Id, t.Name, t.Domain), null, LastOf(state), state?.Unread ?? 0, state?.UnreadMention ?? false,
                online);
        });
        var directChats = states
            .Where(s => s.OtherUserId is { } other && people.ContainsKey(other))
            .Select(s => new ConversationResponse(
                ConversationType.Dm, null, ToContact(people[s.OtherUserId!.Value]), LastOf(s), s.Unread, false));
        var announcements = states.FirstOrDefault(s => s.Type == ConversationType.Announcements);

        // Announcements stays first, even before anyone wrote in it
        return [
            new ConversationResponse(ConversationType.Announcements, null, null, LastOf(announcements), announcements?.Unread ?? 0, false),
            .. teamChats.Concat(directChats)
                .OrderByDescending(c => c.LastMessage?.CreatedAt ?? DateTime.MinValue)
                .ThenBy(c => c.Team?.Name ?? c.Person?.Name),
        ];
    }

    public async Task<MessagesBadgeResponse> BadgeAsync(CancellationToken cancellationToken = default)
    {
        var unread = await UnreadAsync(await ViewerAsync(cancellationToken), cancellationToken);
        return new MessagesBadgeResponse(unread.Sum(c => c.Unread), unread.Any(c => c.UnreadMention));
    }

    public async Task<MessagesBadgeResponse> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var unread = await UnreadAsync(me, cancellationToken);
        var byId = unread.ToDictionary(c => c.Id);
        foreach (var move in await messaging.MoveDeliveredAsync(me.Id, byId.Keys.ToList(), clock.UtcNow, cancellationToken))
        {
            var chat = byId[move.ConversationId];
            await ReceiptsMovedAsync(me, move.ConversationId, chat.Type, chat.TeamId, move.From, move.To, cancellationToken);
        }

        return new MessagesBadgeResponse(unread.Sum(c => c.Unread), unread.Any(c => c.UnreadMention));
    }

    public async Task<MessageReceiptsResponse> ReceiptsAsync(long messageId, CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var message = (await messaging.MessagesByIdAsync([messageId], cancellationToken)).SingleOrDefault();
        var chat = message is null ? null : await ContextAsync(me, message.ConversationId, cancellationToken);
        if (message is null || chat is null)
        {
            throw new NotFoundException(MessageNotFound);
        }

        var announces = chat.Type == ConversationType.Announcements && (await permissions.GetAccessAsync(me.Id, cancellationToken)).Has(PermissionCodes.MessageAnnounce);
        if (!MessageReceipts.CanSee(chat.Type, me.Id, me.Role == RoleCodes.SuperAdmin, announces, message.Sender.Id))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Only whoever sent a message sees who has read it.");
        }

        IReadOnlyList<ChatPerson> active = chat.Type == ConversationType.Dm ? [] : await messaging.ActivePeopleAsync(cancellationToken);
        var markers = await MarkersAsync([chat.ConversationId], cancellationToken);
        IReadOnlyDictionary<long, DateTime> times = chat.Type == ConversationType.Announcements
            ? await messaging.ReadTimesAsync(message.Id, cancellationToken)
            : new Dictionary<long, DateTime>();
        var standing = Standing(chat, message, active, markers);

        ReceiptPersonResponse Person(ChatPerson p, DateTime? at = null) => new(p.Id, p.Name, p.PhotoFileId, at);
        var seen = standing
            .Where(s => s.State == MessageReceipts.State.Seen)
            .Select(s => Person(s.Person, MessageReceipts.SeenAt(message.Id, s.Marker!, times.TryGetValue(s.Person.Id, out var at) ? at : null)))
            .OrderByDescending(p => p.At).ThenBy(p => p.Name)
            .ToList();
        return new MessageReceiptsResponse(
            message.Id,
            Summary(standing.Select(s => s.State).ToList()),
            seen,
            standing.Where(s => s.State == MessageReceipts.State.Delivered).Select(s => Person(s.Person)).ToList(),
            standing.Where(s => s.State == MessageReceipts.State.NotReceived).Select(s => Person(s.Person)).ToList());
    }

    public async Task DeleteAsync(long messageId, CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var message = await messaging.MessageForDeleteAsync(messageId, cancellationToken);
        var chat = message is null ? null : await ContextAsync(me, message.ConversationId, cancellationToken);
        if (message is null || chat is null)
        {
            throw new NotFoundException(MessageNotFound);
        }

        var attached = message.Attachments.Select(a => a.File).ToList();
        MessagingRules.CheckDelete(message.SenderId, me.Id, message.Kind).ThrowIfDenied();

        await messaging.RemoveAsync(message, cancellationToken);
        foreach (var file in attached)
        {
            files.Remove(file);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // The rows are gone first: a stray file on disk is harmless, a row without its file isn't.
        foreach (var file in attached)
        {
            storage.Delete(file.StorageKey);
        }

        await ChatChangedAsync(me, chat, cancellationToken);
    }

    public async Task<int> UnreadCountForAsync(long userId, CancellationToken cancellationToken = default)
    {
        var person = (await messaging.PeopleAsync([userId], cancellationToken)).SingleOrDefault();
        return person is { Status: UserStatus.Active } ? (await UnreadAsync(person, cancellationToken)).Sum(c => c.Unread) : 0;
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
        var conversation = await messaging.TeamConversationAsync(team.Id, cancellationToken);
        return await PageAsync(me, conversation is null ? null : new ChatContext(conversation.Id, ConversationType.Group, team, null), query, cancellationToken);
    }

    public Task<MessageResponse> SendToTeamAsync(long teamId, SendMessageRequest request, CancellationToken cancellationToken = default)
        => SendToTeamAsync(teamId, request, null, cancellationToken);

    public async Task<MessageResponse> SendToTeamAsync(long teamId, SendMessageRequest request, long? protonActionId, CancellationToken cancellationToken)
    {
        var me = await ViewerAsync(cancellationToken);
        var team = await TeamChatAsync(me, teamId, cancellationToken);
        var mentionIds = request.MentionIds.Distinct().ToList();
        var memberIds = (await MembersAsync(team, cancellationToken)).Select(p => p.Id).ToHashSet();
        MessagingRules.CheckMentions(mentionIds, memberIds).ThrowIfDenied();

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var conversation = await TeamConversationForWriteAsync(team, cancellationToken);
        var message = await PostAsync(
            conversation, me.Id, request.Body, MessageKinds.Text, mentionIds, request.AttachmentIds, null, null, cancellationToken, protonActionId);
        await transaction.CommitAsync(cancellationToken);
        TeamChanged(team, memberIds);
        pushes.Message(message.Id, team.Id, memberIds.Where(id => id != me.Id), mentionIds);

        return await OneAsync(me, new ChatContext(conversation.Id, ConversationType.Group, team, null), message.Id, cancellationToken);
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
        var conversation = await messaging.DirectConversationAsync(me.Id, other.Id, cancellationToken);
        return await PageAsync(me, conversation is null ? null : new ChatContext(conversation.Id, ConversationType.Dm, null, other), query, cancellationToken);
    }

    public Task<MessageResponse> SendDirectAsync(long userId, SendMessageRequest request, CancellationToken cancellationToken = default)
        => SendDirectAsync(userId, request, null, cancellationToken);

    public async Task<MessageResponse> SendDirectAsync(long userId, SendMessageRequest request, long? protonActionId, CancellationToken cancellationToken)
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

        var message = await PostAsync(conversation, me.Id, request.Body, MessageKinds.Text, [], request.AttachmentIds, null, null, cancellationToken, protonActionId);
        await transaction.CommitAsync(cancellationToken);
        live.ChatChanged(other.Id, new LiveChat(null, me.Id));
        live.ChatChanged(me.Id, new LiveChat(null, other.Id));
        pushes.Message(message.Id, null, [other.Id], []);

        return await OneAsync(me, new ChatContext(conversation.Id, ConversationType.Dm, null, other), message.Id, cancellationToken);
    }

    public async Task<MessagesBadgeResponse> MarkDirectReadAsync(long userId, CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var other = await ReachableAsync(me, userId, cancellationToken);
        await MarkReadAsync(me, await messaging.DirectConversationAsync(me.Id, other.Id, cancellationToken), cancellationToken);
        live.ChatChanged(me.Id, new LiveChat(null, other.Id));
        return await BadgeAsync(cancellationToken);
    }

    public async Task<MessagePageResponse> AnnouncementsAsync(MessagePageQuery query, CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        var conversation = await messaging.AnnouncementsAsync(cancellationToken);
        return await PageAsync(me, conversation is null ? null : new ChatContext(conversation.Id, ConversationType.Announcements, null, null), query, cancellationToken);
    }

    public Task<MessageResponse> AnnounceAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
        => AnnounceAsync(request, null, cancellationToken);

    public async Task<MessageResponse> AnnounceAsync(SendMessageRequest request, long? protonActionId, CancellationToken cancellationToken)
    {
        var me = await ViewerAsync(cancellationToken);
        MessagingRules.CheckMentions(request.MentionIds, null).ThrowIfDenied();

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var conversation = await messaging.AnnouncementsAsync(cancellationToken)
            ?? throw new InvalidOperationException("The Announcements chat is missing: the Announcements migration creates it.");
        var message = await PostAsync(conversation, me.Id, request.Body, MessageKinds.Text, [], request.AttachmentIds, null, null, cancellationToken, protonActionId);
        await transaction.CommitAsync(cancellationToken);

        var everyone = (await messaging.ActivePeopleAsync(cancellationToken)).Select(p => p.Id).ToList();
        foreach (var id in everyone)
        {
            live.ChatChanged(id, LiveChat.ForAnnouncements);
        }

        pushes.Message(message.Id, null, everyone.Where(id => id != me.Id), [], announcement: true);
        return await OneAsync(me, new ChatContext(conversation.Id, ConversationType.Announcements, null, null), message.Id, cancellationToken);
    }

    public async Task<MessagesBadgeResponse> MarkAnnouncementsReadAsync(CancellationToken cancellationToken = default)
    {
        var me = await ViewerAsync(cancellationToken);
        await MarkReadAsync(me, await messaging.AnnouncementsAsync(cancellationToken), cancellationToken);
        live.ChatChanged(me.Id, LiveChat.ForAnnouncements);
        return await BadgeAsync(cancellationToken);
    }

    public async Task<bool> CanReadConversationAsync(long conversationId, CancellationToken cancellationToken = default)
        => await ContextAsync(await ViewerAsync(cancellationToken), conversationId, cancellationToken) is not null;

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
            throw new BusinessRuleException(ErrorCodes.NoTeamChat, "The entry's author isn't in a department, so there's no department chat to post to.");
        }

        if (!MessagingRules.IsMember(Rule(people[commenterId]), Chat(team)))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You aren't in that department's chat.");
        }

        // The card speaks to the whole chat (the prototype's @all).
        var memberIds = (await MembersAsync(team, cancellationToken)).Select(p => p.Id).ToList();
        var conversation = await TeamConversationForWriteAsync(team, cancellationToken);
        var others = memberIds.Where(id => id != commenterId).ToList();
        var message = await PostAsync(
            conversation, commenterId, body, MessageKinds.TaskComment, others, [], entryId, commentId, cancellationToken);
        TeamChanged(team, memberIds);
        pushes.Message(message.Id, team.Id, others, others);
        return message.Id;
    }

    /// <summary>The signed-in person, who must still be active: an access token outlives a deactivation.</summary>
    private async Task<ChatPerson> ViewerAsync(CancellationToken cancellationToken)
    {
        var me = (await messaging.PeopleAsync([currentUser.Id], cancellationToken)).SingleOrDefault();
        return me is { Status: UserStatus.Active } ? me : throw new ForbiddenException(ErrorCodes.Forbidden, "You don't have permission to do this.");
    }

    /// <summary>The conversation, if the caller may read it now: Announcements, a team chat they are in, or their direct chat with someone they can reach.</summary>
    private async Task<ChatContext?> ContextAsync(ChatPerson me, long conversationId, CancellationToken cancellationToken)
    {
        var conversation = await messaging.ConversationAsync(conversationId, cancellationToken);
        if (conversation?.Type == ConversationType.Announcements)
        {
            return new ChatContext(conversation.Id, ConversationType.Announcements, null, null);
        }

        if (conversation?.TeamId is { } teamId)
        {
            var team = (await VisibleTeamsAsync(me, cancellationToken)).SingleOrDefault(t => t.Id == teamId);
            return team is null ? null : new ChatContext(conversation.Id, ConversationType.Group, team, null);
        }

        if (conversation is null || (conversation.DmUserLowId != me.Id && conversation.DmUserHighId != me.Id))
        {
            return null;
        }

        var otherId = conversation.DmUserLowId == me.Id ? conversation.DmUserHighId!.Value : conversation.DmUserLowId!.Value;
        var other = (await messaging.PeopleAsync([otherId], cancellationToken)).SingleOrDefault();
        return other is not null && MessagingRules.CanReach(Rule(me), Rule(other)) ? new ChatContext(conversation.Id, ConversationType.Dm, null, other) : null;
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

    /// <summary>What the inbox shows unread: in chats the caller is still in, and with people they can still reach.</summary>
    private async Task<IReadOnlyList<UnreadChat>> UnreadAsync(ChatPerson me, CancellationToken cancellationToken)
    {
        var unread = await messaging.UnreadChatsAsync(me.Id, (await VisibleTeamsAsync(me, cancellationToken)).Select(t => t.Id).ToList(), cancellationToken);
        var others = unread.Where(c => c.OtherUserId is not null).Select(c => c.OtherUserId!.Value).ToList();
        var reachable = others.Count == 0
            ? []
            : (await messaging.PeopleAsync(others, cancellationToken)).Where(p => MessagingRules.CanReach(Rule(me), Rule(p))).Select(p => p.Id).ToHashSet();
        return unread.Where(c => c.OtherUserId is null || reachable.Contains(c.OtherUserId.Value)).ToList();
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
        CancellationToken cancellationToken,
        long? protonActionId = null)
    {
        var attachments = new List<MessageAttachment>();
        var types = new List<string>();
        foreach (var fileId in attachmentIds)
        {
            var file = await fileClaims.ClaimAsync(fileId, null, cancellationToken);
            attachments.Add(new MessageAttachment { FileId = file.Id });
            types.Add(file.MimeType);
        }

        MessagingRules.CheckVoiceNotes(types).ThrowIfDenied();
        MessagingRules.CheckNoDocuments(types).ThrowIfDenied();

        var message = new Message
        {
            Conversation = conversation,
            SenderId = senderId,
            Body = string.IsNullOrWhiteSpace(body) ? null : body.Trim(),
            Kind = kind,
            RefEntryId = entryId,
            RefCommentId = commentId,
            ProtonActionId = protonActionId,
            CreatedAt = clock.UtcNow,
            Mentions = mentionIds.Select(id => new MessageMention { UserId = id }).ToList(),
            Attachments = attachments,
        };
        messaging.Add(message);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await messaging.MoveReadMarkerAsync(senderId, conversation.Id, message.Id, clock.UtcNow, cancellationToken);
        return message;
    }

    /// <summary>
    /// Moves the caller's read marker to the chat's newest message. In Announcements it also records when they read
    /// each new one, and whoever sees these messages' receipts is told their ticks changed.
    /// </summary>
    private async Task MarkReadAsync(ChatPerson me, Conversation? conversation, CancellationToken cancellationToken)
    {
        if (conversation is null || await messaging.LastMessageIdAsync(conversation.Id, cancellationToken) is not { } lastId)
        {
            return;
        }

        var before = await messaging.ReadUpToAsync(me.Id, conversation.Id, cancellationToken) ?? 0;
        await messaging.MoveReadMarkerAsync(me.Id, conversation.Id, lastId, clock.UtcNow, cancellationToken);
        if (lastId <= before)
        {
            return;
        }

        if (conversation.Type == ConversationType.Announcements)
        {
            await messaging.RecordReadsAsync(me.Id, conversation.Id, before, lastId, me.CreatedAt, clock.UtcNow, cancellationToken);
        }

        await ReceiptsMovedAsync(me, conversation.Id, conversation.Type, conversation.TeamId, before, lastId, cancellationToken);
    }

    /// <summary>
    /// The caller received or read the messages after <paramref name="from"/> up to <paramref name="to"/>: their senders
    /// see the new ticks, and in team chats and Announcements so do the super admins.
    /// </summary>
    private async Task ReceiptsMovedAsync(
        ChatPerson me,
        long conversationId,
        ConversationType type,
        long? teamId,
        long from,
        long to,
        CancellationToken cancellationToken)
    {
        var told = (await messaging.SendersBetweenAsync(conversationId, from, to, me.Id, cancellationToken)).ToHashSet();
        if (type != ConversationType.Dm)
        {
            told.UnionWith((await messaging.ActivePeopleAsync(cancellationToken)).Where(p => p.Role == RoleCodes.SuperAdmin && p.Id != me.Id).Select(p => p.Id));
        }

        if (told.Count == 0)
        {
            return;
        }

        live.ReceiptsChanged(told, type switch
        {
            ConversationType.Group => new LiveChat(teamId, null),
            ConversationType.Announcements => LiveChat.ForAnnouncements,
            _ => new LiveChat(null, me.Id),
        });
    }

    private async Task<MessagePageResponse> PageAsync(ChatPerson me, ChatContext? chat, MessagePageQuery query, CancellationToken cancellationToken)
    {
        if (chat is null)
        {
            return new MessagePageResponse([], false);
        }

        var newestFirst = await messaging.MessagesAsync(chat.ConversationId, query.Before, query.Limit + 1, cancellationToken);
        var page = newestFirst.Take(query.Limit).Reverse().ToList();
        var receipts = await ReceiptsAsync(me, page.Select(m => (chat, m)).ToList(), null, cancellationToken);
        return new MessagePageResponse(await DescribeAsync(page, receipts, cancellationToken), newestFirst.Count > query.Limit);
    }

    private async Task<MessageResponse> OneAsync(ChatPerson me, ChatContext chat, long messageId, CancellationToken cancellationToken)
    {
        var views = await messaging.MessagesByIdAsync([messageId], cancellationToken);
        var receipts = await ReceiptsAsync(me, views.Select(m => (chat, m)).ToList(), null, cancellationToken);
        return (await DescribeAsync(views, receipts, cancellationToken)).Single();
    }

    /// <summary>The ticks of the messages whose receipts the caller sees (<see cref="MessageReceipts.CanSee"/>), by message.</summary>
    private async Task<IReadOnlyDictionary<long, MessageReceiptResponse>> ReceiptsAsync(
        ChatPerson me,
        IReadOnlyList<(ChatContext Chat, MessageView Message)> items,
        IReadOnlyList<ChatPerson>? active,
        CancellationToken cancellationToken)
    {
        var announces = items.Any(i => i.Chat.Type == ConversationType.Announcements && i.Message.Sender.Id != me.Id)
            && (await permissions.GetAccessAsync(me.Id, cancellationToken)).Has(PermissionCodes.MessageAnnounce);
        var shown = items
            .Where(i => MessageReceipts.CanSee(i.Chat.Type, me.Id, me.Role == RoleCodes.SuperAdmin, announces, i.Message.Sender.Id))
            .ToList();
        if (shown.Count == 0)
        {
            return new Dictionary<long, MessageReceiptResponse>();
        }

        if (active is null && shown.Any(i => i.Chat.Type != ConversationType.Dm))
        {
            active = await messaging.ActivePeopleAsync(cancellationToken);
        }

        var markers = await MarkersAsync(shown.Select(i => i.Chat.ConversationId).Distinct().ToList(), cancellationToken);
        return shown.ToDictionary(
            i => i.Message.Id,
            i => Summary(Standing(i.Chat, i.Message, active ?? [], markers).Select(s => s.State).ToList()));
    }

    private async Task<IReadOnlyDictionary<(long ConversationId, long UserId), MessageReceipts.Marker>> MarkersAsync(
        IReadOnlyCollection<long> conversationIds,
        CancellationToken cancellationToken)
        => (await messaging.MarkersAsync(conversationIds, cancellationToken))
            .ToDictionary(m => (m.ConversationId, m.UserId), m => new MessageReceipts.Marker(m.Read, m.Delivered, m.ReadMovedAt));

    /// <summary>Where each person the message is for stands on it: everyone in the chat now but its sender.</summary>
    private static IReadOnlyList<(ChatPerson Person, MessageReceipts.State State, MessageReceipts.Marker? Marker)> Standing(
        ChatContext chat,
        MessageView message,
        IReadOnlyList<ChatPerson> active,
        IReadOnlyDictionary<(long ConversationId, long UserId), MessageReceipts.Marker> markers)
    {
        IEnumerable<ChatPerson> inChat = chat.Type switch
        {
            ConversationType.Dm => chat.Other is { } other ? [other] : [],
            ConversationType.Group => chat.Team is { } team ? active.Where(p => MessagingRules.IsMember(Rule(p), Chat(team))) : [],
            _ => active,
        };

        return inChat
            .Where(p => MessageReceipts.IsFor(p.Id, p.CreatedAt, message.Sender.Id, message.CreatedAt, chat.Type))
            .Select(p =>
            {
                var marker = markers.GetValueOrDefault((chat.ConversationId, p.Id));
                return (p, MessageReceipts.StateOf(message.Id, marker), marker);
            })
            .ToList();
    }

    private static MessageReceiptResponse Summary(IReadOnlyCollection<MessageReceipts.State> states)
        => new(
            MessageReceipts.TicksOf(states),
            states.Count(s => s == MessageReceipts.State.Seen),
            states.Count(s => s != MessageReceipts.State.NotReceived),
            states.Count);

    /// <summary>Keeps the order given, and adds the entry behind each shared comment and the ticks the caller sees.</summary>
    private async Task<IReadOnlyList<MessageResponse>> DescribeAsync(
        IReadOnlyList<MessageView> messages,
        IReadOnlyDictionary<long, MessageReceiptResponse>? receipts,
        CancellationToken cancellationToken)
    {
        var entryIds = messages.Where(m => m.RefEntryId is not null).Select(m => m.RefEntryId!.Value).Distinct().ToList();
        var shared = entryIds.Count > 0 ? (await entries.ViewsByIdAsync(entryIds, cancellationToken)).ToDictionary(e => e.Id) : [];
        var viewer = shared.Count > 0 ? await permissions.GetAccessAsync(currentUser.Id, cancellationToken) : null;

        return messages.Select(m => new MessageResponse(
                m.Id,
                new ChatPersonResponse(m.Sender.Id, m.Sender.Name, m.Sender.PhotoFileId),
                m.Body,
                m.Kind,
                m.Mentions.Select(p => new ChatPersonResponse(p.Id, p.Name)).ToList(),
                m.Attachments.Select(FileMapper.ToResponse).ToList(),
                m.RefEntryId is { } entryId && shared.TryGetValue(entryId, out var e) ? TaskCard(m, e, viewer!) : null,
                m.CreatedAt,
                m.ViaProton,
                receipts?.GetValueOrDefault(m.Id)))
            .ToList();
    }

    /// <summary>
    /// The entry behind a shared comment. Colleagues' hours stay private: how long it took, and its attachment, show
    /// only to those who may read the entry itself (its author, and whoever sees everyone's work).
    /// </summary>
    private static TaskCommentResponse TaskCard(MessageView m, WorkEntryView e, UserAccess viewer)
    {
        var readable = WorkEntryQueryService.CanRead(viewer, e);
        return new TaskCommentResponse(
            e.Id, m.RefCommentId ?? 0, new ChatPersonResponse(e.UserId, e.UserName), e.ProjectId, e.ProjectTitle, e.Description, e.StartedAt,
            readable ? e.Minutes : null,
            readable && e.Attachment is not null ? FileMapper.ToResponse(e.Attachment) : null);
    }

    /// <summary>Everyone in the chat reloads it.</summary>
    private async Task ChatChangedAsync(ChatPerson me, ChatContext chat, CancellationToken cancellationToken)
    {
        switch (chat.Type)
        {
            case ConversationType.Group:
                TeamChanged(chat.Team!, (await MembersAsync(chat.Team!, cancellationToken)).Select(p => p.Id));
                break;
            case ConversationType.Dm:
                live.ChatChanged(chat.Other!.Id, new LiveChat(null, me.Id));
                live.ChatChanged(me.Id, new LiveChat(null, chat.Other.Id));
                break;
            default:
                foreach (var person in await messaging.ActivePeopleAsync(cancellationToken))
                {
                    live.ChatChanged(person.Id, LiveChat.ForAnnouncements);
                }

                break;
        }
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

    private ContactResponse ToContact(ChatPerson p) => new(
        p.Id, p.Name, p.Role, p.Domain, p.TeamId is { } id ? new TeamSummaryResponse(id, p.TeamName ?? string.Empty) : null, p.Position, p.PhotoFileId,
        presence.IsOnline(p.Id), p.LastSeenAt);
}
