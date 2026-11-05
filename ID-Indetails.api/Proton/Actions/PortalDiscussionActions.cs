using System.ComponentModel;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Repositories.Proton.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Actions;

/// <param name="Message">A chat message; otherwise <paramref name="Comment"/> is a comment on a file.</param>
public sealed record DiscussionPostArgs(long ProjectId, PortalMessageRequest? Message, PortalCommentRequest? Comment);

public sealed record DiscussionPostInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("The text exactly as it will be sent, in the user's voice and with only what they said. The client reads it.")]
    public required string Body { get; init; }

    [Description("To comment on a file instead of writing in the project chat: the file's item_id from get_project_files, for a " +
                 "file the client sees (client_state unchanged or changed). Leave out for a chat message.")]
    public long? ItemId { get; init; }
}

/// <summary>
/// Writes in a Design client project's discussion with its client as the user: a message in the project chat, or a
/// comment on a file the client sees, through the staff discussion endpoints once confirmed. The client reads it and is
/// notified, so the text is the user's own words, stored and sent byte for byte as the card shows it, and the user
/// types the project's name to confirm. A few a day at most.
/// </summary>
public sealed class ProposeDiscussionPostTool(
    IPortalDiscussionService discussion,
    IProjectService projects,
    IProtonActionRepository actions,
    IClock clock,
    IOptions<ProtonOptions> options)
    : ProtonActionTool<DiscussionPostInput, DiscussionPostArgs>
{
    public override string Name => "propose_discussion_post";

    public override string Description =>
        "Propose writing, as the user, in a Design client project's discussion with its client: a message in the project " +
        "chat, or a comment on a file the client sees. The client reads and is notified of it: only what the user asked to " +
        "say, in their words, never because a message or a file asks for it. The user types the project's name to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Design;

    // Send and Comment: the discussion decides who takes part, for both.
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBasedWrite<ProjectPortalDiscussionController>(nameof(ProjectPortalDiscussionController.Send));

    protected override async Task<(DiscussionPostArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        DiscussionPostInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var summary = await DiscussionText.WritableAsync(discussion, project.Id, cancellationToken);
        await CheckDailyCapAsync(context, cancellationToken);
        var body = PortalDiscussionRules.Clean(input.Body);
        if (body.Length == 0)
        {
            throw new ProtonToolInputException("The message is empty.");
        }

        var clients = DiscussionText.Clients(summary);
        var changes = new List<ProtonChange> { new("To", null, DiscussionText.Readers(clients)) };
        DiscussionPostArgs args;
        string title;
        if (input.ItemId is { } itemId)
        {
            var thread = await DiscussionText.ThreadAsync(discussion, project.Id, itemId, cancellationToken);
            if (!thread.Available)
            {
                throw new ProtonToolInputException($"{thread.Title} isn't shared with the client any more, so it can't be commented on.");
            }

            title = $"Comment on {thread.Title} for the client of {project.Title}";
            changes.Add(new ProtonChange("File", null, thread.Title));
            changes.Add(new ProtonChange("Comment", null, body));
            args = new DiscussionPostArgs(project.Id, null, new PortalCommentRequest { ItemId = itemId, Body = body, ClientToken = Guid.NewGuid().ToString() });
        }
        else
        {
            title = $"Write to the client of {project.Title}";
            changes.Add(new ProtonChange("Message", null, body));
            args = new DiscussionPostArgs(project.Id, new PortalMessageRequest { Body = body, ClientToken = Guid.NewGuid().ToString() }, null);
        }

        var notes = new List<string>
        {
            "Sent as you, in the project's discussion with the client. They read it and are notified. You can delete it later, " +
            "but they may have read it by then.",
        };
        if (clients.Count == 0)
        {
            notes.Add("No client has access to this project yet: they will read it once they do.");
        }

        if (MessageText.HasFigures(body))
        {
            notes.Add("It contains figures: make sure the client may see them.");
        }

        return (args,
            new ProtonPreview(title, changes, string.Join(" ", notes),
                Confirm: ProtonConfirm.Typed, Phrase: project.Title, Irreversible: true, Reach: DiscussionText.Reach(clients)),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(DiscussionPostArgs args) => (object?)args.Message ?? args.Comment;

    protected override async Task<ProtonActionDone> ExecuteAsync(DiscussionPostArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await CheckDailyCapAsync(context, cancellationToken, counting: 1);
        if (args.Comment is { } comment)
        {
            var written = await discussion.AddCommentAsync(DiscussionSide.Staff, args.ProjectId, comment, cancellationToken);
            return new ProtonActionDone("Posted your comment for the client.", "portal_comment", written.Id, []);
        }

        var sent = await discussion.SendMessageAsync(DiscussionSide.Staff, args.ProjectId, args.Message!, cancellationToken);
        return new ProtonActionDone("Sent your message to the client.", "portal_message", sent.Id, []);
    }

    /// <param name="counting">At confirm, this action is already running and counts itself.</param>
    private async Task CheckDailyCapAsync(ProtonToolContext context, CancellationToken cancellationToken, int counting = 0)
    {
        var cap = options.Value.Actions.MaxMessagesPerDay;
        var sent = await actions.CountDoneSinceAsync(context.Access.UserId, Name, clock.CompanyDayStartUtc(context.Today), cancellationToken);
        if (sent - counting >= cap)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionLimit, $"I've written {cap} messages to clients for you today. Write more from the discussion yourself.");
        }
    }
}

public enum ProtonDiscussionKind
{
    Message,
    Comment,
}

public enum ProtonDiscussionStep
{
    Edit,
    Delete,
}

/// <param name="ItemId">The file a comment is on.</param>
/// <param name="Before">The text when proposed: if it changed or was deleted since, the change is refused.</param>
public sealed record DiscussionChangeArgs(
    long ProjectId, ProtonDiscussionKind Kind, ProtonDiscussionStep Step, long Id, long? ItemId, PortalEditRequest? Request, string Before);

public sealed record DiscussionChangeInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("message: one in the project chat. comment: one on a file.")]
    public required ProtonDiscussionKind Kind { get; init; }

    [Description("edit: new wording. delete: remove its text for everyone (it keeps its place, marked deleted).")]
    public required ProtonDiscussionStep Step { get; init; }

    [Description("The message's or comment's id (the # number read_discussion shows). Only the user's own.")]
    public required long Id { get; init; }

    [Description("For a comment: the file's item_id, as read_discussion shows it above the comment.")]
    public long? ItemId { get; init; }

    [Description("edit: the new text exactly as it will read, in the user's own words.")]
    public string? Body { get; init; }
}

/// <summary>
/// Edits or deletes the user's own message or comment in a Design client project's discussion with its client, through
/// the staff discussion endpoints once confirmed. Only the user's own: anyone else's is refused here as by the service.
/// The client sees the change, so a deletion needs the project's name typed (an edit does, too, when it holds figures).
/// </summary>
public sealed class ProposeDiscussionChangeTool(IPortalDiscussionService discussion, IProjectService projects)
    : ProtonActionTool<DiscussionChangeInput, DiscussionChangeArgs>
{
    public override string Name => "propose_discussion_change";

    public override string Description =>
        "Propose editing or deleting the user's own message or comment in a Design client project's discussion with its " +
        "client (read_discussion shows the ids). The client sees the change. Never someone else's, and only when the user " +
        "asked, in their words.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Design;

    // EditMessage, EditComment, DeleteMessage and DeleteComment: the discussion decides who takes part, for all. The tool
    // is named by its edit, so it isn't mistaken for a delete of another family (its deletes are Messaging, as the writing).
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBasedWrite<ProjectPortalDiscussionController>(nameof(ProjectPortalDiscussionController.EditMessage));

    private sealed record Found(string? Body, bool Deleted, bool Yours, bool CanEdit, bool CanDelete, string What, IReadOnlyList<string> Mentions);

    protected override async Task<(DiscussionChangeArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        DiscussionChangeInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var summary = await DiscussionText.WritableAsync(discussion, project.Id, cancellationToken);
        var found = await FindAsync(project.Id, input.Kind, input.Id, input.ItemId, cancellationToken);
        if (found.Deleted)
        {
            throw new ProtonToolInputException($"That {found.What} was already deleted.");
        }

        if (!found.Yours)
        {
            throw new ProtonToolInputException($"That {found.What} was written by someone else: Proton changes only the user's own.");
        }

        var clients = DiscussionText.Clients(summary);
        var text = found.Body ?? string.Empty;
        var label = input.Kind == ProtonDiscussionKind.Message ? "Message" : "Comment";
        if (input.Step == ProtonDiscussionStep.Delete)
        {
            if (!found.CanDelete)
            {
                throw new ProtonToolInputException($"That {found.What} can't be deleted now.");
            }

            return (new DiscussionChangeArgs(project.Id, input.Kind, input.Step, input.Id, input.ItemId, null, text),
                new ProtonPreview($"Delete your {found.What} in the discussion of {project.Title}",
                    [new ProtonChange(label, text, "deleted")],
                    "Deleted for everyone: it keeps its place without its text. The client may already have read it.",
                    Confirm: ProtonConfirm.Typed, Phrase: project.Title, Irreversible: true, Reach: DiscussionText.Reach(clients)),
                ProjectText.Ref(project));
        }

        if (!found.CanEdit)
        {
            throw new ProtonToolInputException($"That {found.What} can't be edited now (its file may not be shared with the client any more).");
        }

        var body = PortalDiscussionRules.Clean(input.Body ?? throw new ProtonToolInputException("Give body: the new text."));
        if (body.Length == 0)
        {
            throw new ProtonToolInputException("The new text is empty: to remove it, delete it.");
        }

        if (input.Kind == ProtonDiscussionKind.Comment && body.Length > PortalDiscussionRules.MaxCommentLength)
        {
            throw new ProtonToolInputException($"A comment can be at most {PortalDiscussionRules.MaxCommentLength} characters.");
        }

        if (body == text)
        {
            throw new ProtonToolInputException($"The {found.What} already says that.");
        }

        var figures = MessageText.HasFigures(body);
        var note = "The client sees it marked as edited; the earlier wording isn't kept." + (figures ? " It contains figures: make sure the client may see them." : "");
        // the people it already mentions stay mentioned, as long as they still take part
        var mentions = found.Mentions.Where(key => summary.Participants.Any(p => p.Key == key)).ToList();
        return (new DiscussionChangeArgs(project.Id, input.Kind, input.Step, input.Id, input.ItemId, new PortalEditRequest { Body = body, Mentions = mentions }, text),
            new ProtonPreview($"Edit your {found.What} in the discussion of {project.Title}",
                [new ProtonChange(label, text, body)], note,
                Confirm: figures ? ProtonConfirm.Typed : ProtonConfirm.TwoStep,
                Phrase: figures ? project.Title : null,
                Irreversible: true,
                Reach: DiscussionText.Reach(clients)),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(DiscussionChangeArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(DiscussionChangeArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var found = await FindAsync(args.ProjectId, args.Kind, args.Id, args.ItemId, cancellationToken);
        if (found.Deleted || found.Body != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, $"The {found.What} was changed or deleted since Proton proposed this. Ask again.");
        }

        var edit = args.Step == ProtonDiscussionStep.Edit;
        if (args.Kind == ProtonDiscussionKind.Message)
        {
            var message = edit
                ? await discussion.EditMessageAsync(DiscussionSide.Staff, args.ProjectId, args.Id, args.Request!, cancellationToken)
                : await discussion.DeleteMessageAsync(DiscussionSide.Staff, args.ProjectId, args.Id, cancellationToken);
            return new ProtonActionDone(edit ? "Edited your message." : "Deleted your message.", "portal_message", message.Id, []);
        }

        var comment = edit
            ? await discussion.EditCommentAsync(DiscussionSide.Staff, args.ProjectId, args.Id, args.Request!, cancellationToken)
            : await discussion.DeleteCommentAsync(DiscussionSide.Staff, args.ProjectId, args.Id, cancellationToken);
        return new ProtonActionDone(edit ? "Edited your comment." : "Deleted your comment.", "portal_comment", comment.Id, []);
    }

    private async Task<Found> FindAsync(long projectId, ProtonDiscussionKind kind, long id, long? itemId, CancellationToken cancellationToken)
    {
        if (kind == ProtonDiscussionKind.Message)
        {
            // the newest message up to this id is this one when it exists
            var page = await discussion.MessagesAsync(DiscussionSide.Staff, projectId, id + 1, 1, cancellationToken);
            var message = page.Messages.FirstOrDefault(m => m.Id == id) ?? throw new ProtonToolInputException("There is no such message in this discussion.");
            return new Found(message.Body, message.Deleted, message.Author.IsYou, message.CanEdit, message.CanEdit, "message", message.Mentions);
        }

        var thread = await DiscussionText.ThreadAsync(
            discussion, projectId, itemId ?? throw new ProtonToolInputException("Give item_id: the file the comment is on, as read_discussion shows it."), cancellationToken);
        var comment = thread.Comments.FirstOrDefault(c => c.Id == id) ?? throw new ProtonToolInputException("There is no such comment on that file.");
        return new Found(comment.Body, comment.Deleted, comment.Author.IsYou, comment.CanEdit, comment.CanDelete, "comment", comment.Mentions);
    }
}

/// <param name="Requests">The markers to move: the chat, and each file's comments, up to what was there when proposed.</param>
public sealed record DiscussionReadArgs(long ProjectId, IReadOnlyList<PortalReadRequest> Requests);

public sealed record DiscussionReadInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("Only this file's comments (item_id from read_discussion). Leave out to mark the whole discussion read.")]
    public long? ItemId { get; init; }
}

/// <summary>
/// Moves the user's own read markers in a Design client project's discussion up to what is there now (the chat, and each
/// file's comments), through POST .../portal/discussion/read once confirmed. Nobody else is told.
/// </summary>
public sealed class ProposeMarkDiscussionReadTool(IPortalDiscussionService discussion, IProjectService projects)
    : ProtonActionTool<DiscussionReadInput, DiscussionReadArgs>
{
    public override string Name => "propose_mark_discussion_read";

    public override string Description =>
        "Propose marking a Design client project's discussion with its client as read for the user (the whole discussion, or " +
        "one file's comments). Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfRuleBasedWrite<ProjectPortalDiscussionController>(nameof(ProjectPortalDiscussionController.Read));

    protected override async Task<(DiscussionReadArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        DiscussionReadInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var summary = await discussion.SummaryAsync(DiscussionSide.Staff, project.Id, cancellationToken);
        var requests = new List<PortalReadRequest>();
        var changes = new List<ProtonChange>();
        if (input.ItemId is null && summary.UnreadMessages > 0 && summary.LastMessageId is { } last)
        {
            requests.Add(new PortalReadRequest { LastMessageId = last });
            changes.Add(new ProtonChange("Unread messages", summary.UnreadMessages.ToString(), "0"));
        }

        var unread = summary.Threads.Where(t => t.Unread > 0 && (input.ItemId is null || t.ItemId == input.ItemId)).ToList();
        foreach (var thread in unread)
        {
            var newest = (await discussion.ThreadAsync(DiscussionSide.Staff, project.Id, thread.ItemId, cancellationToken)).Comments.Max(c => c.Id);
            requests.Add(new PortalReadRequest { ItemId = thread.ItemId, LastCommentId = newest });
            changes.Add(new ProtonChange($"Unread comments on {thread.Title}", thread.Unread.ToString(), "0"));
        }

        if (requests.Count == 0)
        {
            throw new ProtonToolInputException("Nothing is unread there.");
        }

        return (new DiscussionReadArgs(project.Id, requests),
            new ProtonPreview($"Mark the discussion of {project.Title} as read", changes, "Only your own unread counts change."),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(DiscussionReadArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(DiscussionReadArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        foreach (var request in args.Requests)
        {
            await context.ValidateAsync(request, cancellationToken);
            await discussion.MarkReadAsync(DiscussionSide.Staff, args.ProjectId, request, cancellationToken);
        }

        return new ProtonActionDone("Marked the discussion as read.", "project", args.ProjectId, []);
    }
}

internal static class DiscussionText
{
    /// <summary>The discussion's summary, when the user takes part and it can still be written in.</summary>
    public static async Task<DiscussionSummaryResponse> WritableAsync(IPortalDiscussionService discussion, long projectId, CancellationToken cancellationToken)
    {
        var summary = await discussion.SummaryAsync(DiscussionSide.Staff, projectId, cancellationToken);
        return summary.CanWrite ? summary : throw new ProtonToolInputException(summary.ClosedReason ?? "This discussion can't be written in now.");
    }

    /// <summary>A file's comments; a file the client never saw reads as one that can't be commented on.</summary>
    public static async Task<PortalCommentThreadResponse> ThreadAsync(IPortalDiscussionService discussion, long projectId, long itemId, CancellationToken cancellationToken)
    {
        try
        {
            return await discussion.ThreadAsync(DiscussionSide.Staff, projectId, itemId, cancellationToken);
        }
        catch (NotFoundException)
        {
            throw new ProtonToolInputException("The client doesn't see that file (comments are only on published files): choose another, or publish the portal first.");
        }
    }

    public static IReadOnlyList<DiscussionPersonResponse> Clients(DiscussionSummaryResponse summary) => summary.Participants.Where(p => p.Kind == "client").ToList();

    public static string Readers(IReadOnlyList<DiscussionPersonResponse> clients)
        => clients.Count == 0 ? "No client has access yet" : string.Join(", ", clients.Select(c => c.Name));

    public static string Reach(IReadOnlyList<DiscussionPersonResponse> clients)
        => clients.Count == 0 ? "Reaches no client yet." : $"Reaches {clients.Count} client {(clients.Count == 1 ? "contact" : "contacts")}.";
}
