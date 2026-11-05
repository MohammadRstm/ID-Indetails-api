using System.ComponentModel;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public enum ProtonBlockedChangeStep
{
    Force,
    Dismiss,
}

/// <param name="Summary">The change in words as the card showed it: it never changes, so a different one means a different change.</param>
public sealed record BlockedChangeArgs(ProtonBlockedChangeStep Step, long ChangeId, string Summary);

public sealed record BlockedChangeInput : ProtonActionInput
{
    [Description("A refused change waiting for a decision. Its id is on the change blocked or log department blocked notification (get_my_notifications); otherwise the user gives it.")]
    public required long BlockedChangeId { get; init; }

    [Description("force: make the refused change anyway, past the profit floor. dismiss: drop it. Only when the user said which. " +
                 "Hours waiting for their logger's department are never forced: they go through once the department is added to the project.")]
    public required ProtonBlockedChangeStep Step { get; init; }
}

/// <summary>
/// Decides a change the profit floor refused, through POST /api/blocked-changes/{id}/force or /dismiss once confirmed.
/// Forcing makes a change that takes the project's profit to zero or below, once: a person's deliberate call, so the user
/// types the project's name. Dismissing drops it. Hours logged by someone whose department isn't on the project wait the
/// same way, but are only dismissed: adding the department lets them through.
/// </summary>
public sealed class ProposeBlockedChangeTool(IBlockedChangeService changes) : ProtonActionTool<BlockedChangeInput, BlockedChangeArgs>
{
    public override string Name => "propose_blocked_change";

    public override string Description =>
        "Propose forcing through, or dropping, a change the profit floor refused (it would take a project's profit to zero or " +
        "below), such as a cost, a payment change or a Design VO that lowers the price. Forcing makes it anyway, once, and the user " +
        "confirms by typing the project's name; dropping keeps the profit as it is. Hours waiting because their logger's department " +
        "isn't on the project can only be dropped: adding the department (propose_project_departments) lets them through. " +
        "Only when the user asked for one of the two.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<BlockedChangesController>(nameof(BlockedChangesController.Force));

    private static readonly ProtonEndpoint Dismiss = ProtonEndpoint.OfWrite<BlockedChangesController>(nameof(BlockedChangesController.Dismiss));

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints => [Endpoint, Dismiss];

    protected override ProtonEndpoint StepOf(BlockedChangeArgs args) => args.Step == ProtonBlockedChangeStep.Force ? Endpoint : Dismiss;

    protected override async Task<(BlockedChangeArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        BlockedChangeInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await context.RequireAsync(input.Step == ProtonBlockedChangeStep.Force ? Endpoint : Dismiss);
        var change = await changes.GetAsync(input.BlockedChangeId, cancellationToken);
        if (change.Status != BlockedChangeStatus.Pending)
        {
            throw new ProtonToolInputException("That change was already forced through or dismissed.");
        }

        var by = change.RequestedBy.Name;
        var target = new ProtonRef(ProtonRefType.Project, change.ProjectId, change.ProjectTitle, change.Domain);
        var waiting = change.Kind == BlockedChangeKind.WorkEntryDepartment;
        if (waiting && input.Step == ProtonBlockedChangeStep.Force)
        {
            throw new ProtonToolInputException(
                $"These hours wait for {by}'s department, not the profit floor: they go through on their own once it is added to {change.ProjectTitle} (propose_project_departments). They can only be dropped.");
        }

        if (input.Step == ProtonBlockedChangeStep.Dismiss)
        {
            return (new BlockedChangeArgs(input.Step, change.Id, change.Summary),
                new ProtonPreview($"Drop the refused change on {change.ProjectTitle}",
                    [new ProtonChange("Refused change", change.Summary, "dropped")],
                    waiting
                        ? $"Logged by {by}. The hours are dropped and never counted, even if the department is added later; a change is decided once."
                        : $"Asked by {by}. Nothing is made and the project's profit stays as it is; a change is decided once.",
                    Irreversible: true),
                target);
        }

        return (new BlockedChangeArgs(input.Step, change.Id, change.Summary),
            new ProtonPreview($"Force a refused change on {change.ProjectTitle}",
                [new ProtonChange("Refused change", change.Summary, "made anyway")],
                $"Asked by {by}. It was refused because it takes the project's profit to zero or below, and its profit is {MoneyText.Amount(change.ProfitNow, "USD")} now. " +
                "Forcing makes it once, as it was asked, and a change is decided once.",
                Confirm: ProtonConfirm.Typed, Phrase: change.ProjectTitle, Irreversible: true),
            target);
    }

    protected override object? RequestOf(BlockedChangeArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(BlockedChangeArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        // the user decided on the card they saw: it must still be that change, and still waiting
        var current = await changes.GetAsync(args.ChangeId, cancellationToken);
        if (current.Status != BlockedChangeStatus.Pending || current.Summary != args.Summary)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The refused change was decided or changed since Proton proposed this. Ask again.");
        }

        var target = new ProtonRef(ProtonRefType.Project, current.ProjectId, current.ProjectTitle, current.Domain);
        if (args.Step == ProtonBlockedChangeStep.Dismiss)
        {
            await changes.DismissAsync(args.ChangeId, cancellationToken);
            return new ProtonActionDone($"Dropped the refused change on {current.ProjectTitle}.", "blocked_change", args.ChangeId, [target]);
        }

        await changes.ForceAsync(args.ChangeId, cancellationToken);
        return new ProtonActionDone($"Made the refused change on {current.ProjectTitle}.", "blocked_change", args.ChangeId, [target]);
    }
}
