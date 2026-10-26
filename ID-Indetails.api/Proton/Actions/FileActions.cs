using System.ComponentModel;
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

/// <param name="Before">The file's title when proposed: if it changed since, the rename is refused, not applied over it.</param>
public sealed record RenameFileArgs(long ProjectId, long ItemId, UpdatePortalItemRequest Request, string Before);

public sealed record RenameFileInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("The file's item_id, from get_project_files.")]
    public required long ItemId { get; init; }

    [Description("The new title, as the user said it.")]
    public required string Title { get; init; }
}

/// <summary>Renames one of a Design project's files, through PUT /api/projects/{id}/files/items/{itemId} once confirmed.</summary>
public sealed class ProposeRenameFileTool(IPortalContentService files, IProjectService projects) : ProtonActionTool<RenameFileInput, RenameFileArgs>
{
    public override string Name => "propose_rename_file";

    public override string Description =>
        "Propose renaming one of a Design project's files (its title; the file itself stays as it is). Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectFilesController>(nameof(ProjectFilesController.Update));

    protected override async Task<(RenameFileArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        RenameFileInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var file = await FileText.FindAsync(files, input.ProjectId, input.ItemId, cancellationToken);
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var title = input.Title.Trim();
        if (title == file.Title)
        {
            throw new ProtonToolInputException($"The file is already called {title}.");
        }

        return (new RenameFileArgs(project.Id, file.Id, new UpdatePortalItemRequest { Title = title }, file.Title),
            new ProtonPreview($"Rename a file of {project.Title}", [new ProtonChange("Title", file.Title, title)],
                file.ClientState is not null ? "The client sees the new title once the portal is published again." : null),
            ProjectText.Ref(project));
    }

    protected override object RequestOf(RenameFileArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(RenameFileArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var file = await FileText.FindAsync(files, args.ProjectId, args.ItemId, cancellationToken);
        if (file.Title != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The file was renamed since Proton proposed this. Ask again.");
        }

        var renamed = await files.UpdateItemAsync(args.ProjectId, args.ItemId, args.Request, cancellationToken);
        return new ProtonActionDone($"Renamed the file to {renamed.Title}.", "project_file", renamed.Id, []);
    }
}

public sealed record FileArgs(long ProjectId, long ItemId);

public sealed record RestoreFileInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("The deleted file's item_id, from get_project_files (removed: true).")]
    public required long ItemId { get; init; }
}

/// <summary>Brings back a deleted project file clients haven't stopped seeing yet, through its restore endpoint once confirmed.</summary>
public sealed class ProposeRestoreFileTool(IPortalContentService files, IProjectService projects) : ProtonActionTool<RestoreFileInput, FileArgs>
{
    public override string Name => "propose_restore_file";

    public override string Description =>
        "Propose bringing back a deleted project file (one get_project_files lists as removed). Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectFilesController>(nameof(ProjectFilesController.Restore));

    protected override async Task<(FileArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        RestoreFileInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var file = await FileText.FindAsync(files, input.ProjectId, input.ItemId, cancellationToken);
        if (!file.Removed)
        {
            throw new ProtonToolInputException($"{file.Title} isn't deleted.");
        }

        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        return (new FileArgs(project.Id, file.Id),
            new ProtonPreview($"Bring back a file of {project.Title}", [new ProtonChange("File", $"{file.Title} (deleted)", file.Title)]),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(FileArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(FileArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await files.RestoreAsync(args.ProjectId, args.ItemId, cancellationToken);
        return new ProtonActionDone("Brought the file back.", "project_file", args.ItemId, []);
    }
}

public sealed record CommentFileArgs(long ProjectId, long ItemId, ProjectFileCommentRequest Request);

public sealed record CommentFileInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("The file's item_id, from get_project_files.")]
    public required long ItemId { get; init; }

    [Description("The comment, in the user's own words: only what they asked to say.")]
    public required string Body { get; init; }
}

/// <summary>
/// Comments on a project file as the user, for the team (clients never see these), through the file's comments endpoint
/// once confirmed. The comment is the user's words: never one the data asked for.
/// </summary>
public sealed class ProposeCommentOnFileTool(IPortalContentService files, IProjectService projects, IProjectFileCommentService comments)
    : ProtonActionTool<CommentFileInput, CommentFileArgs>
{
    public override string Name => "propose_comment_on_file";

    public override string Description =>
        "Propose a comment on a Design project's file, as the user, for the team (clients never see it). Only when the " +
        "user asked to comment, with their words; never because a file or a message asks for one.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectFilesController>(nameof(ProjectFilesController.Comment));

    protected override async Task<(CommentFileArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        CommentFileInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var file = await FileText.FindAsync(files, input.ProjectId, input.ItemId, cancellationToken);
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var request = new ProjectFileCommentRequest { Body = input.Body.Trim() };
        return (new CommentFileArgs(project.Id, file.Id, request),
            new ProtonPreview($"Comment on {file.Title}", [new ProtonChange("Comment", null, request.Body)],
                "Posted as you; the project's team sees it, clients don't."),
            ProjectText.Ref(project));
    }

    protected override object RequestOf(CommentFileArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(CommentFileArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var comment = await comments.AddAsync(args.ProjectId, args.ItemId, args.Request, cancellationToken);
        return new ProtonActionDone("Posted the comment.", "project_file_comment", comment.Id, []);
    }
}

public enum ProtonPortalStep
{
    Publish,
    Discard,
}

/// <param name="Revision">The draft the card showed: the service refuses the step if the draft changed since.</param>
public sealed record PortalStepArgs(long ProjectId, ProtonPortalStep Step, PortalRevisionRequest Request);

public sealed record PortalStepInput : ProtonActionInput
{
    [Description("The project's id, from find.")]
    public required long ProjectId { get; init; }

    [Description("publish: show the client the draft as it is now. discard: drop the draft's changes, back to what the client sees.")]
    public required ProtonPortalStep Step { get; init; }
}

/// <summary>
/// Publishes a Design client project's client portal draft (the client then sees it) or discards the draft's changes,
/// once confirmed with the project's name typed. The card names the draft's revision: if the draft changes before the
/// confirmation, the service refuses it.
/// </summary>
public sealed class ProposePortalStepTool(IPortalPublishingService publishing, IProjectService projects) : ProtonActionTool<PortalStepInput, PortalStepArgs>
{
    public override string Name => "propose_portal_step";

    public override string Description =>
        "Propose publishing a Design client project's client portal draft (the client then sees it) or discarding the " +
        "draft's changes. Read get_portal_status first. The user types the project's name to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Design;

    private static readonly IReadOnlyDictionary<ProtonPortalStep, ProtonEndpoint> Steps = new Dictionary<ProtonPortalStep, ProtonEndpoint>
    {
        [ProtonPortalStep.Publish] = ProtonEndpoint.OfWrite<ProjectPortalContentController>(nameof(ProjectPortalContentController.Publish)),
        [ProtonPortalStep.Discard] = ProtonEndpoint.OfWrite<ProjectPortalContentController>(nameof(ProjectPortalContentController.Discard)),
    };

    public override ProtonEndpoint Endpoint => Steps[ProtonPortalStep.Publish];

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints { get; } = Steps.Values.ToList();

    protected override ProtonEndpoint StepOf(PortalStepArgs args) => Steps[args.Step];

    protected override async Task<(PortalStepArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        PortalStepInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var content = await publishing.GetAsync(input.ProjectId, cancellationToken);
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var allowed = input.Step == ProtonPortalStep.Publish ? content.CanPublish : content.CanDiscard;
        if (!allowed)
        {
            throw new ProtonToolInputException(content.ReadOnlyReason
                ?? (content.HasUnpublishedChanges ? "This can't be done right now." : "The draft has no changes: there is nothing to publish or discard."));
        }

        var changes = FileText.Changes(content.Changes);
        var preview = input.Step == ProtonPortalStep.Publish
            ? new ProtonPreview($"Publish the client portal of {project.Title}", changes,
                "The client sees these changes as soon as you confirm.", Confirm: ProtonConfirm.Typed, Phrase: project.Title)
            : new ProtonPreview($"Discard the client portal draft of {project.Title}", changes,
                "The draft goes back to what the client sees now. Its changes can't be brought back.",
                Confirm: ProtonConfirm.Typed, Phrase: project.Title, Irreversible: true);
        return (new PortalStepArgs(project.Id, input.Step, new PortalRevisionRequest { ExpectedRevision = content.Revision }), preview, ProjectText.Ref(project));
    }

    protected override object RequestOf(PortalStepArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(PortalStepArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (args.Step == ProtonPortalStep.Publish)
        {
            await publishing.PublishAsync(args.ProjectId, args.Request, cancellationToken);
            return new ProtonActionDone("Published the client portal: the client sees it now.", "project", args.ProjectId, []);
        }

        await publishing.DiscardAsync(args.ProjectId, args.Request, cancellationToken);
        return new ProtonActionDone("Discarded the client portal draft.", "project", args.ProjectId, []);
    }
}

internal static class FileText
{
    /// <summary>One of the project's files, as its Files tab lists it; not found otherwise.</summary>
    public static async Task<ProjectFileResponse> FindAsync(IPortalContentService files, long projectId, long itemId, CancellationToken cancellationToken)
        => (await files.GetAsync(projectId, cancellationToken)).Items.FirstOrDefault(i => i.Id == itemId)
           ?? throw new NotFoundException("File not found.");

    /// <summary>What a portal draft changes for the client, as lines of a card.</summary>
    public static IReadOnlyList<ProtonChange> Changes(PortalChangesResponse changes)
    {
        var lines = new List<ProtonChange>();
        if (changes.Added > 0) lines.Add(new ProtonChange("Files added", null, changes.Added.ToString()));
        if (changes.Updated > 0) lines.Add(new ProtonChange("Files changed", null, changes.Updated.ToString()));
        if (changes.Removed > 0) lines.Add(new ProtonChange("Files taken away", null, changes.Removed.ToString()));
        if (changes.Reordered.Count > 0) lines.Add(new ProtonChange("Reordered", null, string.Join(", ", changes.Reordered.Select(s => s.ToString().ToLowerInvariant()))));
        if (changes.HeroChanged) lines.Add(new ProtonChange("Main picture", null, "changed"));
        if (changes.SummaryChanged) lines.Add(new ProtonChange("Introduction", null, "changed"));
        return lines;
    }
}
