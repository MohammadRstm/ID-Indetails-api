using System.ComponentModel;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Teams.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Actions;

public sealed record CommentWorkEntryArgs(long WorkEntryId, AddWorkEntryCommentRequest Request);

public sealed record CommentWorkEntryInput : ProtonActionInput
{
    [Description("From list_work_entries or get_work_entry.")]
    public required long WorkEntryId { get; init; }

    [Description("The comment, in the user's words.")]
    public required string Body { get; init; }

    [Description("Also post it to the author's team chat, mentioning everyone there. Only when the user asked for that.")]
    public bool SendToGroup { get; init; }
}

/// <summary>
/// Comments on a work entry, through POST /api/work-entries/{id}/comments once confirmed. Sharing it to the author's
/// team chat too is a message to all of them: it needs Messaging switched on, a question the user typed, and the
/// team's name typed to confirm.
/// </summary>
public sealed class ProposeCommentWorkEntryTool(
    IWorkEntryQueryService workEntries,
    IWorkEntryCommentService comments,
    IEmployeeService employees,
    ITeamService teams,
    IClock clock,
    IOptions<ProtonOptions> options)
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
        var request = new AddWorkEntryCommentRequest { Body = input.Body.Trim(), SendToGroup = input.SendToGroup };
        var preview = new ProtonPreview(
            $"Comment on {entry.Person.Name}'s work",
            [
                new ProtonChange("Entry", null, $"{clock.Local(entry.StartedAt)}, {entry.Project.Title}: {ProtonText.Cut(entry.Description, 120)}"),
                new ProtonChange("Comment", null, request.Body),
            ],
            entry.Person.Id == context.Access.UserId ? null : $"{entry.Person.Name} is notified.");
        if (input.SendToGroup)
        {
            CheckGroupAllowed(context.Turn);
            var team = (await employees.GetAsync(entry.Person.Id, cancellationToken)).Team
                ?? throw new ProtonToolInputException($"{entry.Person.Name} isn't in a team, so there is no team chat to share it to.");
            var members = (await TeamText.FindAsync(teams, team.Id, cancellationToken)).MemberCount;
            preview = preview with
            {
                Changes = [.. preview.Changes, new ProtonChange("Also posted to", null, $"{team.Name} team chat, mentioning everyone")],
                Confirm = ProtonConfirm.Typed,
                Phrase = team.Name,
                Irreversible = true,
                Reach = $"Reaches {members} {(members == 1 ? "person" : "people")}.",
            };
        }

        return (new CommentWorkEntryArgs(entry.Id, request), preview, new ProtonRef(ProtonRefType.Project, entry.Project.Id, entry.Project.Title, DomainCodes.Design));
    }

    protected override object RequestOf(CommentWorkEntryArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(CommentWorkEntryArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (args.Request.SendToGroup && !options.Value.Actions.IsOn(ProtonFamily.Messaging))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionsOff, "Sending messages is switched off for Proton now. You can share the comment in the app.");
        }

        var comment = await comments.AddAsync(args.WorkEntryId, args.Request, cancellationToken);
        return new ProtonActionDone(comment.SentToGroup ? "Posted the comment and shared it to the team chat." : "Posted the comment.", "work_entry_comment", comment.Id, []);
    }

    /// <summary>The comment family is Core; sharing it is a message, held to the Messaging family's rules.</summary>
    private void CheckGroupAllowed(ProtonTurnOrigin turn)
    {
        if (!options.Value.Actions.IsOn(ProtonFamily.Messaging))
        {
            throw new ProtonToolInputException("Sharing to a team chat isn't switched on for Proton yet: post the comment alone, or share it in the app.");
        }

        if (turn != ProtonTurnOrigin.Typed)
        {
            throw new ProtonToolInputException("Sharing to a team chat needs the user to ask for it in their own words.");
        }
    }
}
