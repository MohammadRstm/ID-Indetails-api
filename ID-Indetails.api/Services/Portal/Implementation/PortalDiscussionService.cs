using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Portal.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.api.Services.Portal.Implementation;

/// <summary>
/// A Design client project's discussion. Who takes part is read from the project, its files, its work and its client
/// access on every call (<see cref="PortalDiscussionRules"/>), never from what the browser says. Clients see and
/// comment on published files only. Notifications name who and where, never the text: staff through the usual
/// notifications, clients through their own. Every change signals both sides' live connections, without content.
/// </summary>
public sealed class PortalDiscussionService(
    ICurrentUser currentUser,
    ICurrentPortalAccount currentAccount,
    IPortalDiscussionRepository discussion,
    INotificationSender notifications,
    ILiveUpdates live,
    IUnitOfWork unitOfWork,
    IClock clock) : IPortalDiscussionService
{
    private const string NotAvailable = "This discussion isn't available.";

    /// <summary>The project, the caller, and everyone taking part.</summary>
    private sealed record Room(
        DiscussionProject Project, DiscussionPerson Me, IReadOnlyList<DiscussionPerson> Staff, IReadOnlyList<DiscussionPerson> Clients)
    {
        public IReadOnlyList<DiscussionPerson> Everyone => [.. Staff, .. Clients];

        public bool CanWrite => PortalDiscussionRules.CheckWritable(Project.Status) is null;
    }

    /// <summary>What was written, for its notifications.</summary>
    private sealed record Written(long? MessageId, long? CommentId, long? ItemId, string? ItemTitle, long? UploaderId);

    public async Task<DiscussionSummaryResponse> SummaryAsync(DiscussionSide side, long projectId, CancellationToken cancellationToken = default)
        => await SummaryAsync(await OpenAsync(side, projectId, cancellationToken), cancellationToken);

    public async Task<PortalMessagePageResponse> MessagesAsync(
        DiscussionSide side, long projectId, long? before, int? limit, CancellationToken cancellationToken = default)
    {
        var room = await OpenAsync(side, projectId, cancellationToken);
        var size = PortalDiscussionRules.PageSize(limit);
        var rows = await discussion.MessagesAsync(projectId, before, size + 1, cancellationToken);
        var page = rows.Take(size).Reverse().ToList();
        var mentions = await discussion.MessageMentionsAsync(page.Select(m => m.Id).ToList(), cancellationToken);
        return new PortalMessagePageResponse(page.Select(m => Describe(room, m, mentions[m.Id])).ToList(), rows.Count > size);
    }

    public async Task<PortalChatMessageResponse> SendMessageAsync(
        DiscussionSide side, long projectId, PortalMessageRequest request, CancellationToken cancellationToken = default)
    {
        var room = await OpenAsync(side, projectId, cancellationToken);
        Throw(PortalDiscussionRules.CheckWritable(room.Project.Status));

        // the same message sent again (a retry) is the first one, and nobody hears of it twice
        if (await discussion.FindMessageAsync(room.Me, request.ClientToken, cancellationToken) is { } sent)
        {
            return sent.ProjectId == projectId ? await MessageAsync(room, sent.Id, cancellationToken) : throw new NotFoundException(NotAvailable);
        }

        var mentioned = Mentioned(request.Mentions, room);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var message = new PortalMessage
            {
                ProjectId = projectId,
                AuthorUserId = room.Me.Side == DiscussionSide.Staff ? room.Me.Id : null,
                AuthorAccountId = room.Me.Side == DiscussionSide.Client ? room.Me.Id : null,
                Body = PortalDiscussionRules.Clean(request.Body),
                ClientToken = request.ClientToken,
                CreatedAt = clock.UtcNow,
            };
            discussion.Add(message);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            AddMentions(mentioned, message.Id, null);
            await NotifyAsync(room, new Written(message.Id, null, null, null, null), mentioned, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await MessageAsync(room, message.Id, cancellationToken);
        }
    }

    public async Task<PortalChatMessageResponse> EditMessageAsync(
        DiscussionSide side, long projectId, long messageId, PortalEditRequest request, CancellationToken cancellationToken = default)
    {
        var room = await OpenAsync(side, projectId, cancellationToken);
        Throw(PortalDiscussionRules.CheckWritable(room.Project.Status));
        var message = await discussion.GetMessageAsync(projectId, messageId, cancellationToken) ?? throw new NotFoundException(NotAvailable);
        if (!IsAuthor(message.AuthorUserId, message.AuthorAccountId, room.Me))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Only its author can change a message.");
        }

        var mentioned = Mentioned(request.Mentions, room);
        message.Body = PortalDiscussionRules.Clean(request.Body);
        message.EditedAt = clock.UtcNow;
        var added = await ReplaceMentionsAsync(mentioned, messageId, null, cancellationToken);
        await NotifyMentionsAsync(room, new Written(messageId, null, null, null, null), added, cancellationToken);
        Signal(room);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await MessageAsync(room, messageId, cancellationToken);
    }

    public async Task<PortalCommentThreadResponse> ThreadAsync(DiscussionSide side, long projectId, long itemId, CancellationToken cancellationToken = default)
    {
        var room = await OpenAsync(side, projectId, cancellationToken);
        var comments = await discussion.CommentsAsync(projectId, itemId, cancellationToken);
        var published = await discussion.PublishedItemsAsync(projectId, cancellationToken);
        var shown = published.GetValueOrDefault(itemId);
        if (shown is null && comments.Count == 0)
        {
            // never published here, or another project's: nothing to say
            throw new NotFoundException(NotAvailable);
        }

        var mentions = await discussion.CommentMentionsAsync(comments.Select(c => c.Id).ToList(), cancellationToken);
        return new PortalCommentThreadResponse(
            itemId,
            shown?.Title ?? comments[^1].ItemTitle,
            shown?.FileName ?? comments[^1].FileName,
            shown is not null,
            comments.Select(c => Describe(room, c, mentions[c.Id], shown)).ToList());
    }

    public async Task<PortalCommentResponse> AddCommentAsync(
        DiscussionSide side, long projectId, PortalCommentRequest request, CancellationToken cancellationToken = default)
    {
        var room = await OpenAsync(side, projectId, cancellationToken);
        Throw(PortalDiscussionRules.CheckWritable(room.Project.Status));

        if (await discussion.FindCommentAsync(room.Me, request.ClientToken, cancellationToken) is { } sent)
        {
            return sent.ProjectId == projectId ? await CommentAsync(room, sent.Id, cancellationToken) : throw new NotFoundException(NotAvailable);
        }

        // only on a file clients can see now
        var item = (await discussion.PublishedItemsAsync(projectId, cancellationToken)).GetValueOrDefault(request.ItemId)
            ?? throw new NotFoundException("File not found.");
        var mentioned = Mentioned(request.Mentions, room);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var comment = new PortalComment
            {
                ProjectId = projectId,
                ItemId = item.ItemId,
                FileId = item.FileId,
                ItemTitle = item.Title,
                FileName = item.FileName,
                AuthorUserId = room.Me.Side == DiscussionSide.Staff ? room.Me.Id : null,
                AuthorAccountId = room.Me.Side == DiscussionSide.Client ? room.Me.Id : null,
                Body = PortalDiscussionRules.Clean(request.Body),
                ClientToken = request.ClientToken,
                CreatedAt = clock.UtcNow,
            };
            discussion.Add(comment);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            AddMentions(mentioned, null, comment.Id);
            await NotifyAsync(room, new Written(null, comment.Id, item.ItemId, item.Title, item.UploadedById), mentioned, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await CommentAsync(room, comment.Id, cancellationToken);
        }
    }

    public async Task<PortalCommentResponse> EditCommentAsync(
        DiscussionSide side, long projectId, long commentId, PortalEditRequest request, CancellationToken cancellationToken = default)
    {
        var room = await OpenAsync(side, projectId, cancellationToken);
        Throw(PortalDiscussionRules.CheckWritable(room.Project.Status));
        var comment = await OwnCommentAsync(room, commentId, cancellationToken);
        var item = (await discussion.PublishedItemsAsync(projectId, cancellationToken)).GetValueOrDefault(comment.ItemId);
        if (item is null)
        {
            throw new BusinessRuleException(ErrorCodes.PortalDiscussionClosed, "This file is no longer shared, so its comments can't change.");
        }

        var body = PortalDiscussionRules.Clean(request.Body);
        if (body.Length > PortalDiscussionRules.MaxCommentLength)
        {
            throw new RequestValidationException(
                ErrorCodes.ValidationFailed, "body", $"A comment can be at most {PortalDiscussionRules.MaxCommentLength} characters.");
        }

        var mentioned = Mentioned(request.Mentions, room);
        comment.Body = body;
        comment.EditedAt = clock.UtcNow;
        var added = await ReplaceMentionsAsync(mentioned, null, commentId, cancellationToken);
        await NotifyMentionsAsync(room, new Written(null, commentId, comment.ItemId, item.Title, item.UploadedById), added, cancellationToken);
        Signal(room);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await CommentAsync(room, commentId, cancellationToken);
    }

    public async Task<PortalCommentResponse> DeleteCommentAsync(
        DiscussionSide side, long projectId, long commentId, CancellationToken cancellationToken = default)
    {
        var room = await OpenAsync(side, projectId, cancellationToken);
        Throw(PortalDiscussionRules.CheckWritable(room.Project.Status));
        var comment = await OwnCommentAsync(room, commentId, cancellationToken, allowDeleted: true);
        if (comment.DeletedAt is null)
        {
            comment.Body = null;
            comment.DeletedAt = clock.UtcNow;
            foreach (var mention in await discussion.MentionsAsync(null, commentId, cancellationToken))
            {
                discussion.Remove(mention);
            }

            Signal(room);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await CommentAsync(room, commentId, cancellationToken);
    }

    public async Task<DiscussionSummaryResponse> MarkReadAsync(
        DiscussionSide side, long projectId, PortalReadRequest request, CancellationToken cancellationToken = default)
    {
        var room = await OpenAsync(side, projectId, cancellationToken);
        var read = await discussion.GetReadAsync(projectId, room.Me, cancellationToken);
        if (read is null)
        {
            read = new PortalRead
            {
                ProjectId = projectId,
                UserId = room.Me.Side == DiscussionSide.Staff ? room.Me.Id : null,
                AccountId = room.Me.Side == DiscussionSide.Client ? room.Me.Id : null,
            };
            discussion.Add(read);
        }

        read.LastMessageId = PortalDiscussionRules.MoveMarker(
            read.LastMessageId, request.LastMessageId, await discussion.NewestMessageIdAsync(projectId, cancellationToken));
        read.LastCommentId = PortalDiscussionRules.MoveMarker(
            read.LastCommentId, request.LastCommentId, await discussion.NewestCommentIdAsync(projectId, cancellationToken));
        read.UpdatedAt = clock.UtcNow;

        // the person's other tabs and devices update their unread counts
        if (room.Me.Side == DiscussionSide.Staff)
        {
            live.PortalChanged(projectId, [room.Me.Id], []);
        }
        else
        {
            live.PortalChanged(projectId, [], [room.Me.Id]);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await SummaryAsync(room, cancellationToken);
    }

    /// <summary>The project's discussion with the caller in it; a 404 for anyone not taking part.</summary>
    private async Task<Room> OpenAsync(DiscussionSide side, long projectId, CancellationToken cancellationToken)
    {
        var project = await discussion.ProjectAsync(projectId, cancellationToken);
        if (project is null || !PortalDiscussionRules.HasDiscussion(project))
        {
            throw new NotFoundException(NotAvailable);
        }

        var staff = await discussion.StaffAsync(project, cancellationToken);
        var clients = await discussion.ClientsAsync(projectId, cancellationToken);
        var me = side == DiscussionSide.Staff
            ? staff.SingleOrDefault(p => p.Id == currentUser.Id)
            : clients.SingleOrDefault(p => p.Id == currentAccount.Id);
        return me is null ? throw new NotFoundException(NotAvailable) : new Room(project, me, staff, clients);
    }

    private async Task<DiscussionSummaryResponse> SummaryAsync(Room room, CancellationToken cancellationToken)
    {
        var projectId = room.Project.Id;
        var read = await discussion.GetReadAsync(projectId, room.Me, cancellationToken);
        var comments = await discussion.CommentsAsync(projectId, null, cancellationToken);
        var published = await discussion.PublishedItemsAsync(projectId, cancellationToken);
        var readComments = read?.LastCommentId ?? 0;

        var threads = comments
            .GroupBy(c => c.ItemId)
            .Select(thread =>
            {
                var last = thread.MaxBy(c => c.Id)!;
                var shown = published.GetValueOrDefault(thread.Key);
                return new CommentThreadSummaryResponse(
                    thread.Key,
                    shown?.Title ?? last.ItemTitle,
                    shown?.FileName ?? last.FileName,
                    shown is not null,
                    thread.Count(),
                    thread.Count(c => c.Id > readComments && c.DeletedAt is null && c.Author.Key != room.Me.Key),
                    last.CreatedAt);
            })
            .OrderByDescending(t => t.LastAt)
            .ToList();

        var closed = PortalDiscussionRules.CheckWritable(room.Project.Status);
        return new DiscussionSummaryResponse
        {
            ProjectId = projectId,
            CanWrite = closed is null,
            ClosedReason = closed?.Message,
            UnreadMessages = await discussion.UnreadMessagesAsync(projectId, read?.LastMessageId, room.Me, cancellationToken),
            UnreadComments = threads.Sum(t => t.Unread),
            LastMessageId = await discussion.NewestMessageIdAsync(projectId, cancellationToken),
            LastCommentId = comments.Count == 0 ? null : comments.Max(c => c.Id),
            Participants = room.Everyone.Select(p => Person(p, room.Me)).ToList(),
            Threads = threads,
        };
    }

    private async Task<PortalChatMessageResponse> MessageAsync(Room room, long messageId, CancellationToken cancellationToken)
    {
        var row = await discussion.MessageRowAsync(room.Project.Id, messageId, cancellationToken) ?? throw new NotFoundException(NotAvailable);
        var mentions = await discussion.MessageMentionsAsync([messageId], cancellationToken);
        return Describe(room, row, mentions[messageId]);
    }

    private async Task<PortalCommentResponse> CommentAsync(Room room, long commentId, CancellationToken cancellationToken)
    {
        var row = await discussion.CommentRowAsync(room.Project.Id, commentId, cancellationToken) ?? throw new NotFoundException(NotAvailable);
        var mentions = await discussion.CommentMentionsAsync([commentId], cancellationToken);
        var shown = (await discussion.PublishedItemsAsync(room.Project.Id, cancellationToken)).GetValueOrDefault(row.ItemId);
        return Describe(room, row, mentions[commentId], shown);
    }

    /// <summary>The caller's own comment in this project; someone else's can't be changed.</summary>
    private async Task<PortalComment> OwnCommentAsync(Room room, long commentId, CancellationToken cancellationToken, bool allowDeleted = false)
    {
        var comment = await discussion.GetCommentAsync(room.Project.Id, commentId, cancellationToken) ?? throw new NotFoundException(NotAvailable);
        if (!IsAuthor(comment.AuthorUserId, comment.AuthorAccountId, room.Me))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Only its author can change a comment.");
        }

        return comment.DeletedAt is null || allowDeleted
            ? comment
            : throw new BusinessRuleException(ErrorCodes.PortalDiscussionClosed, "This comment was deleted.");
    }

    private static IReadOnlyList<DiscussionPerson> Mentioned(IReadOnlyList<string> keys, Room room)
        => PortalDiscussionRules.Mentioned(keys, room.Everyone, room.Me)
            ?? throw new RequestValidationException(ErrorCodes.ValidationFailed, "mentions", "Only people taking part in this discussion can be mentioned.");

    private void AddMentions(IEnumerable<DiscussionPerson> people, long? messageId, long? commentId)
    {
        foreach (var person in people)
        {
            discussion.Add(new PortalMention
            {
                MessageId = messageId,
                CommentId = commentId,
                UserId = person.Side == DiscussionSide.Staff ? person.Id : null,
                AccountId = person.Side == DiscussionSide.Client ? person.Id : null,
            });
        }
    }

    /// <summary>Makes the mentions these people, and returns the ones newly mentioned.</summary>
    private async Task<IReadOnlyList<DiscussionPerson>> ReplaceMentionsAsync(
        IReadOnlyList<DiscussionPerson> people, long? messageId, long? commentId, CancellationToken cancellationToken)
    {
        var current = await discussion.MentionsAsync(messageId, commentId, cancellationToken);
        var keys = people.Select(p => p.Key).ToHashSet();
        foreach (var mention in current.Where(m => !keys.Contains(KeyOf(m))))
        {
            discussion.Remove(mention);
        }

        var had = current.Select(KeyOf).ToHashSet();
        var added = people.Where(p => !had.Contains(p.Key)).ToList();
        AddMentions(added, messageId, commentId);
        return added;
    }

    /// <summary>
    /// Tells the others: a client's message to every staff member taking part, a client's comment to the project's
    /// manager and the file's uploader (or everyone on the staff side if neither takes part), and anything the team
    /// writes to the project's clients. Mentioned people get a mention instead. All rolling, so a busy chat is one
    /// notification each, and none carries the text.
    /// </summary>
    private async Task NotifyAsync(Room room, Written what, IReadOnlyList<DiscussionPerson> mentioned, CancellationToken cancellationToken)
    {
        await NotifyMentionsAsync(room, what, mentioned, cancellationToken);
        var project = room.Project;
        var mentionedKeys = mentioned.Select(p => p.Key).ToHashSet();
        var payload = Payload(room, what);

        if (room.Me.Side == DiscussionSide.Client)
        {
            var staff = room.Staff.Where(p => !mentionedKeys.Contains(p.Key)).ToList();
            if (what.MessageId is not null)
            {
                await notifications.SendDigestAsync(
                    new NotificationDraft(NotificationKinds.PortalClientMessage, null, project.DomainCode, project.Id, null, null, payload),
                    $"portal_chat:{project.Id}", staff.Select(p => p.Id), cancellationToken);
            }
            else
            {
                var concerned = staff.Where(p => p.Id == project.OwnerId || p.Id == what.UploaderId).ToList();
                await notifications.SendDigestAsync(
                    new NotificationDraft(NotificationKinds.PortalClientComment, null, project.DomainCode, project.Id, "item", what.ItemId, payload),
                    $"portal_comments:{what.ItemId}", (concerned.Count > 0 ? concerned : staff).Select(p => p.Id), cancellationToken);
            }
        }
        else
        {
            var clients = room.Clients.Where(p => !mentionedKeys.Contains(p.Key)).Select(p => p.Id).ToList();
            foreach (var accountId in clients)
            {
                await NotifyClientAsync(
                    accountId, project.Id,
                    what.MessageId is not null ? PortalNotificationKind.TeamMessage : PortalNotificationKind.TeamComment,
                    what.ItemId,
                    what.MessageId is not null ? $"chat:{project.Id}" : $"comments:{what.ItemId}",
                    rolling: true, payload, cancellationToken);
            }

            live.PortalNotificationsChanged(clients);
        }

        Signal(room);
    }

    /// <summary>A mention, once per message or comment and person.</summary>
    private async Task NotifyMentionsAsync(Room room, Written what, IReadOnlyList<DiscussionPerson> mentioned, CancellationToken cancellationToken)
    {
        if (mentioned.Count == 0)
        {
            return;
        }

        var project = room.Project;
        var payload = Payload(room, what);
        var key = what.MessageId is { } messageId ? $"m{messageId}" : $"c{what.CommentId}";
        var staff = mentioned.Where(p => p.Side == DiscussionSide.Staff).Select(p => p.Id).ToList();
        if (staff.Count > 0)
        {
            await notifications.SendOnceAsync(
                new NotificationDraft(
                    NotificationKinds.PortalMention,
                    room.Me.Side == DiscussionSide.Staff ? room.Me.Id : null,
                    project.DomainCode, project.Id,
                    what.MessageId is not null ? "message" : "comment",
                    what.ItemId, payload),
                $"portal_mention:{key}", staff, cancellationToken);
        }

        var clients = mentioned.Where(p => p.Side == DiscussionSide.Client).Select(p => p.Id).ToList();
        foreach (var accountId in clients)
        {
            await NotifyClientAsync(accountId, project.Id, PortalNotificationKind.Mention, what.ItemId, $"mention:{key}", rolling: false, payload, cancellationToken);
        }

        live.PortalNotificationsChanged(clients);
    }

    /// <summary>
    /// A client's notification. A rolling one adds to the unread one with its key (or starts again at 1 once read); a
    /// one-off is never sent twice.
    /// </summary>
    private async Task NotifyClientAsync(
        long accountId, long projectId, PortalNotificationKind kind, long? subjectId, string key, bool rolling, object payload,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var json = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
        var existing = await discussion.GetNotificationForUpdateAsync(accountId, key, cancellationToken);
        if (existing is null)
        {
            discussion.Add(new PortalNotification
            {
                AccountId = accountId,
                ProjectId = projectId,
                Kind = kind,
                SubjectId = subjectId,
                Payload = json,
                DigestKey = key,
                Count = 1,
                CreatedAt = now,
            });
            return;
        }

        if (!rolling)
        {
            return;
        }

        existing.Count = existing.ReadAt is null ? existing.Count + 1 : 1;
        existing.ReadAt = null;
        existing.CreatedAt = now;
        existing.Payload = json;
        existing.SubjectId = subjectId;
    }

    /// <summary>Everyone taking part sees the change on their open pages.</summary>
    private void Signal(Room room)
        => live.PortalChanged(room.Project.Id, room.Staff.Select(p => p.Id), room.Clients.Select(p => p.Id));

    private static object Payload(Room room, Written what)
        => new { projectTitle = room.Project.Title, authorName = room.Me.Name, itemTitle = what.ItemTitle };

    private PortalChatMessageResponse Describe(Room room, DiscussionMessageRow row, IEnumerable<string> mentions)
        => new()
        {
            Id = row.Id,
            Author = Person(row.Author, room.Me),
            Body = row.Body,
            Mentions = mentions.ToList(),
            CreatedAt = row.CreatedAt,
            EditedAt = row.EditedAt,
            CanEdit = room.CanWrite && row.Author.Key == room.Me.Key,
        };

    private PortalCommentResponse Describe(Room room, DiscussionCommentRow row, IEnumerable<string> mentions, PublishedItemFacts? shown)
    {
        var mine = row.Author.Key == room.Me.Key && row.DeletedAt is null && room.CanWrite;
        return new PortalCommentResponse
        {
            Id = row.Id,
            ItemId = row.ItemId,
            Author = Person(row.Author, room.Me),
            Body = row.Body,
            Deleted = row.DeletedAt is not null,
            Mentions = mentions.ToList(),
            FileName = row.FileName,
            OnEarlierVersion = shown is not null && shown.FileId != row.FileId,
            CreatedAt = row.CreatedAt,
            EditedAt = row.EditedAt,
            CanEdit = mine && shown is not null,
            CanDelete = mine,
        };
    }

    private static DiscussionPersonResponse Person(DiscussionPerson person, DiscussionPerson me)
        => new(person.Key, person.Name, person.Side == DiscussionSide.Staff ? "staff" : "client", person.Key == me.Key);

    private static bool IsAuthor(long? userId, long? accountId, DiscussionPerson me)
        => me.Side == DiscussionSide.Staff ? userId == me.Id : accountId == me.Id;

    private static string KeyOf(PortalMention mention)
        => mention.UserId is { } staff
            ? PortalDiscussionRules.KeyOf(DiscussionSide.Staff, staff)
            : PortalDiscussionRules.KeyOf(DiscussionSide.Client, mention.AccountId!.Value);

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
