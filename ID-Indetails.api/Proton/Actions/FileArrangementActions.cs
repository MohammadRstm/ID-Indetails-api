using System.ComponentModel;
using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Portal.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/// <param name="Before">The section's files in order when proposed: if they moved since, the new order isn't put over them.</param>
public sealed record ReorderFilesArgs(long ProjectId, PortalSection Section, PortalOrderRequest Request, IReadOnlyList<long> Before);

public sealed record ReorderFilesInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("presentation, model (shown as 3D) or drawing.")]
    public required PortalSection Section { get; init; }

    [Description("Every file of that section (not the deleted ones), each item_id from get_project_files once, in the new order.")]
    public required IReadOnlyList<long> ItemIds { get; init; }
}

/// <summary>
/// Puts one section of a Design project's files in a new order, through PUT /api/projects/{id}/files/sections/{section}/order
/// once confirmed. The order is the whole section, so a section changed since the card is refused.
/// </summary>
public sealed class ProposeReorderFilesTool(IPortalContentService files, IProjectService projects) : ProtonActionTool<ReorderFilesInput, ReorderFilesArgs>
{
    public override string Name => "propose_reorder_files";

    public override string Description =>
        "Propose putting one section (presentation, 3D or drawings) of a Design project's files in a new order. List every " +
        "file of the section once, from get_project_files. For people who manage project files. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectFilesController>(nameof(ProjectFilesController.Reorder));

    protected override async Task<(ReorderFilesArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ReorderFilesInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var list = await files.GetAsync(project.Id, cancellationToken);
        if (!list.CanManage)
        {
            throw new ProtonToolInputException("Only people who manage project files can change their order.");
        }

        if (list.ReadOnlyReason is { } reason)
        {
            throw new ProtonToolInputException(reason);
        }

        var current = Section(list, input.Section);
        if (!PortalPublishRules.IsWholeOrder(current.Select(i => i.Id).ToList(), input.ItemIds))
        {
            throw new ProtonToolInputException(
                $"List every file of the section exactly once. They are: {string.Join("; ", current.Select(i => $"{i.Id} ({i.Title})"))}.");
        }

        if (input.ItemIds.SequenceEqual(current.Select(i => i.Id)))
        {
            throw new ProtonToolInputException("They are already in that order.");
        }

        var titles = current.ToDictionary(i => i.Id, i => i.Title);
        var section = input.Section.ToString().ToLowerInvariant();
        return (new ReorderFilesArgs(project.Id, input.Section, new PortalOrderRequest { ItemIds = input.ItemIds }, current.Select(i => i.Id).ToList()),
            new ProtonPreview($"Reorder the {section} files of {project.Title}",
                [new ProtonChange("Order", string.Join(", ", current.Select(i => i.Title)), string.Join(", ", input.ItemIds.Select(id => titles[id])))],
                current.Any(i => i.ClientState is not null) ? "The client sees the new order once the client portal is published." : null),
            ProjectText.Ref(project));
    }

    protected override object RequestOf(ReorderFilesArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(ReorderFilesArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = Section(await files.GetAsync(args.ProjectId, cancellationToken), args.Section);
        if (!current.Select(i => i.Id).SequenceEqual(args.Before))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The files were moved or changed since Proton proposed this. Ask again.");
        }

        await files.ReorderAsync(args.ProjectId, args.Section, args.Request, cancellationToken);
        return new ProtonActionDone("Put the files in the new order.", "project", args.ProjectId, []);
    }

    private static List<ProjectFileResponse> Section(ProjectFilesResponse list, PortalSection section)
        => list.Items.Where(i => !i.Removed && i.Section == section).OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
}

/// <param name="Before">Whether the file was chosen for the client when proposed.</param>
public sealed record ChooseForClientArgs(long ProjectId, long ItemId, ProjectFileForClientRequest Request, bool Before);

public sealed record ChooseForClientInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("The file's item_id, from get_project_files.")]
    public required long ItemId { get; init; }

    [Description("true: choose the file for the client. false: take it back from the client.")]
    public required bool ForClient { get; init; }
}

/// <summary>
/// Chooses a Design client project's file for the client, or takes it back, through PUT .../files/items/{itemId}/for-client
/// once confirmed. Only the draft changes: the client sees it once the portal is published.
/// </summary>
public sealed class ProposeFileForClientTool(IPortalContentService files, IProjectService projects) : ProtonActionTool<ChooseForClientInput, ChooseForClientArgs>
{
    public override string Name => "propose_file_for_client";

    public override string Description =>
        "Propose choosing one of a Design client project's files for the client, or taking it back. Only the draft changes: " +
        "the client sees the change once the client portal is published. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectFilesController>(nameof(ProjectFilesController.SetForClient));

    protected override async Task<(ChooseForClientArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ChooseForClientInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var list = await files.GetAsync(project.Id, cancellationToken);
        if (!list.IsClientProject)
        {
            throw new ProtonToolInputException("Only a client project has a client portal: this project is internal.");
        }

        if (!list.CanChooseForClient)
        {
            throw new ProtonToolInputException(list.ReadOnlyReason ?? "This user can't choose what the client sees: that needs permission to manage the client portal.");
        }

        var file = list.Items.FirstOrDefault(i => i.Id == input.ItemId && !i.Removed) ?? throw new NotFoundException("File not found.");
        if (file.ForClient == input.ForClient)
        {
            throw new ProtonToolInputException(input.ForClient ? $"{file.Title} is already chosen for the client." : $"{file.Title} isn't chosen for the client.");
        }

        return (new ChooseForClientArgs(project.Id, file.Id, new ProjectFileForClientRequest { ForClient = input.ForClient }, file.ForClient),
            new ProtonPreview(
                input.ForClient ? $"Choose {file.Title} for the client of {project.Title}" : $"Take {file.Title} back from the client of {project.Title}",
                [new ProtonChange("For the client", file.ForClient ? "yes" : "no", input.ForClient ? "yes" : "no")],
                input.ForClient
                    ? "Only the client portal's draft changes. The client sees the file once the portal is published."
                    : "Only the client portal's draft changes. The client keeps seeing what is published until the portal is published again."),
            ProjectText.Ref(project));
    }

    protected override object RequestOf(ChooseForClientArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(ChooseForClientArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var file = await FileText.FindAsync(files, args.ProjectId, args.ItemId, cancellationToken);
        if (file.ForClient != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The file was chosen or taken back since Proton proposed this. Ask again.");
        }

        await files.SetForClientAsync(args.ProjectId, args.ItemId, args.Request, cancellationToken);
        return new ProtonActionDone(
            args.Request.ForClient ? "Chose the file for the client." : "Took the file back from the client.", "project_file", args.ItemId, []);
    }
}

/// <param name="Before">The comment's text when proposed: if it changed since, the edit is refused.</param>
public sealed record EditFileCommentArgs(long ProjectId, long ItemId, long CommentId, ProjectFileCommentRequest Request, string Before);

public sealed record FileCommentInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("The file's item_id, from get_project_files.")]
    public required long ItemId { get; init; }

    [Description("The comment's comment_id, from get_file_comments. Only the user's own for an edit or a delete.")]
    public required long CommentId { get; init; }
}

public sealed record EditFileCommentInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("The file's item_id, from get_project_files.")]
    public required long ItemId { get; init; }

    [Description("The user's own comment's comment_id, from get_file_comments.")]
    public required long CommentId { get; init; }

    [Description("The new text exactly as it will read, in the user's own words.")]
    public required string Body { get; init; }
}

/// <summary>Edits the user's own comment on a project file, for the team, through PUT .../files/comments/{id} once confirmed.</summary>
public sealed class ProposeEditFileCommentTool(IProjectFileCommentService comments, IProjectService projects)
    : ProtonActionTool<EditFileCommentInput, EditFileCommentArgs>
{
    public override string Name => "propose_edit_file_comment";

    public override string Description =>
        "Propose changing the user's own comment on a Design project's file (the team sees it; clients never do), in their " +
        "own words. Only their own comments. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectFilesController>(nameof(ProjectFilesController.EditComment));

    protected override async Task<(EditFileCommentArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        EditFileCommentInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var comment = await FileCommentText.FindAsync(comments, project.Id, input.ItemId, input.CommentId, cancellationToken);
        if (!comment.CanEdit)
        {
            throw new ProtonToolInputException("Only the author can edit a comment, and not on an archived project: that one isn't the user's to change.");
        }

        var body = ProjectFileRules.Clean(input.Body);
        if (body == comment.Body)
        {
            throw new ProtonToolInputException("The comment already says that.");
        }

        return (new EditFileCommentArgs(project.Id, comment.ItemId, comment.Id, new ProjectFileCommentRequest { Body = body }, comment.Body),
            new ProtonPreview($"Edit your comment on a file of {project.Title}", [new ProtonChange("Comment", comment.Body, body)],
                "The team sees the new wording; clients never see these comments."),
            ProjectText.Ref(project));
    }

    protected override object RequestOf(EditFileCommentArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(EditFileCommentArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var comment = await FileCommentText.FindAsync(comments, args.ProjectId, args.ItemId, args.CommentId, cancellationToken);
        if (comment.Body != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The comment was edited since Proton proposed this. Ask again.");
        }

        var edited = await comments.EditAsync(args.ProjectId, args.CommentId, args.Request, cancellationToken);
        return new ProtonActionDone("Edited your comment.", "project_file_comment", edited.Id, []);
    }
}

public sealed record FixCommentArgs(long ProjectId, long ItemId, long CommentId, ProjectFileFixedRequest Request);

public sealed record FixCommentInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("The file's item_id, from get_project_files.")]
    public required long ItemId { get; init; }

    [Description("The comment's comment_id, from get_file_comments.")]
    public required long CommentId { get; init; }

    [Description("true: mark it fixed. false: open it again.")]
    public required bool Fixed { get; init; }
}

/// <summary>Marks a comment on a project file fixed, or open again, through PUT .../files/comments/{id}/fixed once confirmed.</summary>
public sealed class ProposeSetCommentFixedTool(IProjectFileCommentService comments, IProjectService projects) : ProtonActionTool<FixCommentInput, FixCommentArgs>
{
    public override string Name => "propose_set_comment_fixed";

    public override string Description =>
        "Propose marking a comment on a Design project's file fixed, or open again. For the file's uploader or people who " +
        "manage project files. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectFilesController>(nameof(ProjectFilesController.SetFixed));

    protected override async Task<(FixCommentArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        FixCommentInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var comment = await FileCommentText.FindAsync(comments, project.Id, input.ItemId, input.CommentId, cancellationToken);
        if (!comment.CanFix)
        {
            throw new ProtonToolInputException("Only the person who uploaded the file, or someone who manages project files, can mark its comments fixed (and not on an archived project).");
        }

        if ((comment.FixedAt is not null) == input.Fixed)
        {
            throw new ProtonToolInputException(input.Fixed ? "That comment is already fixed." : "That comment is already open.");
        }

        return (new FixCommentArgs(project.Id, comment.ItemId, comment.Id, new ProjectFileFixedRequest { Fixed = input.Fixed }),
            new ProtonPreview(input.Fixed ? $"Mark a comment fixed on a file of {project.Title}" : $"Open a comment again on a file of {project.Title}",
                [new ProtonChange("Comment", comment.Body, input.Fixed ? "fixed" : "open")]),
            ProjectText.Ref(project));
    }

    protected override object RequestOf(FixCommentArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(FixCommentArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var comment = await comments.SetFixedAsync(args.ProjectId, args.CommentId, args.Request, cancellationToken);
        return new ProtonActionDone(args.Request.Fixed ? "Marked the comment fixed." : "Opened the comment again.", "project_file_comment", comment.Id, []);
    }
}

/// <param name="Before">The comment's text when proposed: if it was edited since, the delete is refused.</param>
public sealed record DeleteFileCommentArgs(long ProjectId, long ItemId, long CommentId, string Before);

/// <summary>Deletes the user's own comment on a project file for good, through DELETE .../files/comments/{id} once confirmed, with the project's name typed.</summary>
public sealed class ProposeDeleteFileCommentTool(IProjectFileCommentService comments, IProjectService projects)
    : ProtonActionTool<FileCommentInput, DeleteFileCommentArgs>
{
    public override string Name => "propose_delete_file_comment";

    public override string Description =>
        "Propose deleting the user's own comment on a Design project's file, for good. Only their own comments. The user " +
        "types the project's name to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectFilesController>(nameof(ProjectFilesController.DeleteComment));

    protected override async Task<(DeleteFileCommentArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        FileCommentInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var comment = await FileCommentText.FindAsync(comments, project.Id, input.ItemId, input.CommentId, cancellationToken);
        if (!comment.CanDelete)
        {
            throw new ProtonToolInputException("Only the author can delete a comment, and not on an archived project: that one isn't the user's to delete.");
        }

        return (new DeleteFileCommentArgs(project.Id, comment.ItemId, comment.Id, comment.Body),
            new ProtonPreview($"Delete your comment on a file of {project.Title}", [new ProtonChange("Comment", comment.Body, "deleted")],
                "Deleted for good: it can't be brought back.", Confirm: ProtonConfirm.Typed, Phrase: project.Title, Irreversible: true),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(DeleteFileCommentArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(DeleteFileCommentArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var comment = await FileCommentText.FindAsync(comments, args.ProjectId, args.ItemId, args.CommentId, cancellationToken);
        if (comment.Body != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The comment was edited since Proton proposed this. Ask again.");
        }

        await comments.DeleteAsync(args.ProjectId, args.CommentId, cancellationToken);
        return new ProtonActionDone("Deleted your comment.", "project_file_comment", args.CommentId, []);
    }
}

internal static class FileCommentText
{
    /// <summary>One comment of a file, as its thread lists it; not found otherwise.</summary>
    public static async Task<ProjectFileCommentResponse> FindAsync(
        IProjectFileCommentService comments, long projectId, long itemId, long commentId, CancellationToken cancellationToken)
        => (await comments.ListAsync(projectId, itemId, cancellationToken)).FirstOrDefault(c => c.Id == commentId)
           ?? throw new NotFoundException("Comment not found.");
}
