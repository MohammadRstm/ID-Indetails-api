using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public sealed record CommentWorkEntryArgs(long WorkEntryId, AddWorkEntryCommentRequest Request);

public sealed record CommentWorkEntryInput : ProtonActionInput
{
    [Description("From list_work_entries or get_work_entry.")]
    public required long WorkEntryId { get; init; }

    [Description("The comment, in the user's words.")]
    public required string Body { get; init; }
}

/// <summary>
/// Comments on a work entry, through POST /api/work-entries/{id}/comments once confirmed. Only on the entry: posting
/// it to the author's team chat too is a message, which Proton doesn't send.
/// </summary>
public sealed class ProposeCommentWorkEntryTool(IWorkEntryQueryService workEntries, IWorkEntryCommentService comments, IClock clock)
    : ProtonActionTool<CommentWorkEntryInput, CommentWorkEntryArgs>
{
    public override string Name => "propose_comment_work_entry";

    public override string Description =>
        "Propose commenting on someone's logged work entry; its author is notified. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Low;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<WorkEntriesController>(nameof(WorkEntriesController.AddComment));

    protected override async Task<(CommentWorkEntryArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        CommentWorkEntryInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var entry = (await workEntries.GetAsync(input.WorkEntryId, cancellationToken)).Entry;
        var request = new AddWorkEntryCommentRequest { Body = input.Body.Trim(), SendToGroup = false };
        var preview = new ProtonPreview(
            $"Comment on {entry.Person.Name}'s work",
            [
                new ProtonChange("Entry", null, $"{clock.Local(entry.StartedAt)}, {entry.Project.Title}: {ProtonText.Cut(entry.Description, 120)}"),
                new ProtonChange("Comment", null, request.Body),
            ],
            entry.Person.Id == context.Access.UserId ? null : $"{entry.Person.Name} is notified.");
        return (new CommentWorkEntryArgs(entry.Id, request), preview, new ProtonRef(ProtonRefType.Project, entry.Project.Id, entry.Project.Title, DomainCodes.Design));
    }

    protected override object RequestOf(CommentWorkEntryArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(CommentWorkEntryArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var comment = await comments.AddAsync(args.WorkEntryId, args.Request, cancellationToken);
        return new ProtonActionDone("Posted the comment.", "work_entry_comment", comment.Id, []);
    }
}
