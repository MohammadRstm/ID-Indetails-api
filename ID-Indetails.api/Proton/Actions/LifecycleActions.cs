using System.ComponentModel;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Requirements.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Actions;

/// <summary>The status changes that lock or unlock a project's revenue.</summary>
public enum ProtonLifecycleChange
{
    Complete,
    Archive,
    Reopen,
}

public sealed record ProjectLifecycleInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("complete finishes an active or held project; archive puts any project away; reopen makes a completed or archived one active again.")]
    public required ProtonLifecycleChange Change { get; init; }

    [Description("Completing only, and only when the user asked for it: what becomes of everything the client still owes. " +
                 "excused is a discount agreed with the client, written_off is revenue lost; either lowers the price to what was " +
                 "received. Left out, it stays owed.")]
    public OwedSettlement? SettleOwed { get; init; }

    [Description("Completing only, and only when the user asked for it: true makes what the client paid beyond the price part of it.")]
    public bool? KeepPaidAhead { get; init; }
}

/// <summary>
/// Completes, archives or reopens a project, through POST /api/projects/{id}/complete, /archive or /reopen once
/// confirmed. Completing and archiving lock its revenue and tick its open requirements; reopening unlocks them. The user
/// types the project's name to confirm. The card shows what the client still owes or paid ahead; settling either when
/// completing changes the price, so it is held to the Money family's rules: switched on, a question the user typed, the
/// permission that changes the project's money, and the amount typed to confirm.
/// </summary>
public sealed class ProposeProjectLifecycleTool(
    IProjectService projects, IProjectStatusService statuses, IRequirementService requirements, IOptions<ProtonOptions> options)
    : ProtonActionTool<ProjectLifecycleInput, ProjectStatusArgs>
{
    public override string Name => "propose_project_lifecycle";

    public override string Description =>
        "Propose completing, archiving or reopening a project. Completing or archiving locks its revenue and ticks its " +
        "open requirements; reopening unlocks them. The card shows what the client still owes or paid ahead; when " +
        "completing, settle_owed excuses or writes off what is owed and keep_paid_ahead keeps an overpayment, only if the " +
        "user asked. The user confirms by typing the project's name, or the amount settled.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    // Complete, Archive and Reopen share their policies.
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.Complete));

    protected override async Task<(ProjectStatusArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ProjectLifecycleInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var action = input.Change switch
        {
            ProtonLifecycleChange.Complete => ProjectStatusRules.Action.Complete,
            ProtonLifecycleChange.Archive => ProjectStatusRules.Action.Archive,
            _ => ProjectStatusRules.Action.Reopen,
        };
        var (transition, denial) = ProjectStatusRules.Plan(project.Status, action);
        denial.ThrowIfDenied();

        var completes = transition!.Finalises && action == ProjectStatusRules.Action.Complete;
        // what the client owes or paid ahead, TVA included: only for whoever sees the project's money
        var billing = project.BillingType == BillingType.Client ? project.Money?.Billing : null;
        var keep = input.KeepPaidAhead ?? false;
        var balance = keep || input.SettleOwed is not null
            ? SettlementOf(project, billing, completes, keep, input.SettleOwed, context)
            : (decimal?)null;

        string effect;
        if (transition.Finalises)
        {
            var open = (await requirements.ListAsync(project.Id, cancellationToken)).Items.Count(r => !r.IsDone);
            effect = $"Its revenue is locked{(open > 0 ? $", and its {open} open requirement(s) are ticked done" : "")}.";
        }
        else if (transition.Reopens)
        {
            effect = "Its revenue is unlocked, and the requirements completing ticked are open again.";
        }
        else
        {
            effect = "It leaves the active lists.";
        }

        var changes = new List<ProtonChange> { new("Status", ProjectText.Status(project.Status), ProjectText.Status(transition.To)) };
        if (balance is { } settled && project.Money is { } money)
        {
            changes.Add(input.SettleOwed is { } settlement
                ? new ProtonChange("Owed by the client (TVA included)", MoneyText.Amount(settled, "USD"),
                    settlement == OwedSettlement.Excused ? "nothing: excused, a discount agreed with the client" : "nothing: written off as revenue lost")
                : new ProtonChange("Paid ahead (TVA included)", MoneyText.Amount(settled, "USD"), "nothing: kept as part of the price"));
            changes.Add(new ProtonChange("Price with TVA", MoneyText.Amount(money.RevenueWithTva, "USD"), MoneyText.Amount(billing!.Received, "USD")));
        }
        else if (completes && billing is not null)
        {
            // completing alone leaves the balance as it is: the card says so, so nobody locks the revenue without seeing it
            effect += billing.Owed > 0
                ? $" The client still owes {MoneyText.Amount(billing.Owed, "USD")} (TVA included); it stays owed unless you excuse it or write it off."
                : billing.PaidAhead > 0
                    ? $" The client paid {MoneyText.Amount(billing.PaidAhead, "USD")} beyond the price (TVA included); it stays paid ahead unless you keep it as part of the price."
                    : "";
        }

        var title = input.Change switch
        {
            ProtonLifecycleChange.Complete => $"Complete {project.Title}",
            ProtonLifecycleChange.Archive => $"Archive {project.Title}",
            _ => $"Reopen {project.Title}",
        };
        return (new ProjectStatusArgs(project.Id, action, keep, input.SettleOwed, balance),
            new ProtonPreview(title, changes,
                $"{effect} The project's domain is notified.",
                Confirm: ProtonConfirm.Typed,
                Phrase: balance is { } amount ? MoneyText.Phrase(amount) : project.Title),
            ProjectText.Ref(project));
    }

    /// <summary>
    /// The balance a completion would settle, once it is known the user may settle it here: completing only, Money
    /// switched on, a typed question, the permission that changes the project's money, and a balance to settle.
    /// </summary>
    private decimal SettlementOf(
        ProjectResponse project, ProjectBillingResponse? billing, bool completes, bool keep, OwedSettlement? settle, ProtonToolContext context)
    {
        if (!completes)
        {
            throw new ProtonToolInputException("Only completing a project can settle what its client owes or keep what they paid ahead.");
        }

        if (keep && settle is not null)
        {
            throw new ProtonToolInputException("Either settle what the client owes or keep what they paid ahead: a client can't do both at once.");
        }

        if (!options.Value.Actions.IsOn(ProtonFamily.Money))
        {
            throw new ProtonToolInputException(
                "Settling a client's balance changes the project's price, which isn't switched on for Proton yet: complete the project alone, or settle it in the app.");
        }

        if (context.Turn != ProtonTurnOrigin.Typed)
        {
            throw new ProtonToolInputException("Settling a client's balance needs the user to ask for it in their own words.");
        }

        var moneyWriter = project.Domain == DomainCodes.Execution ? PermissionCodes.ProjectPricingManage : PermissionCodes.FinancialWrite;
        if (!context.Access.Has(moneyWriter))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Only whoever changes this project's money can settle what its client owes or paid ahead.");
        }

        if (billing is null)
        {
            throw new ProtonToolInputException("This project has no client balance this user can see: it is internal work, or its money isn't visible to them.");
        }

        var balance = keep ? billing.PaidAhead : billing.Owed;
        return balance > 0
            ? balance
            : throw new ProtonToolInputException(keep ? "The client hasn't paid beyond the price." : "The client owes nothing on this project.");
    }

    protected override object? RequestOf(ProjectStatusArgs args) => null;

    protected override async Task<ProtonActionDone> ExecuteAsync(ProjectStatusArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (args.KeepPaidAhead || args.SettleOwed is not null)
        {
            if (!options.Value.Actions.IsOn(ProtonFamily.Money))
            {
                throw new BusinessRuleException(ErrorCodes.ProtonActionsOff,
                    "Changing a project's price is switched off for Proton now. You can complete it and settle the balance in the app.");
            }

            // the user typed the amount the card showed: a balance that moved since would settle something else
            var billing = (await projects.GetAsync(args.ProjectId, cancellationToken)).Money?.Billing;
            if ((args.SettleOwed is not null ? billing?.Owed : billing?.PaidAhead) != args.Balance)
            {
                throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The client's balance changed since I proposed this. Ask me again.");
            }
        }

        var project = await statuses.ChangeAsync(args.ProjectId, args.Action, cancellationToken, args.KeepPaidAhead, args.SettleOwed);
        var settled = args.Balance is { } amount
            ? args.SettleOwed switch
            {
                OwedSettlement.Excused => $" The {MoneyText.Amount(amount, "USD")} the client owed is excused.",
                OwedSettlement.WrittenOff => $" The {MoneyText.Amount(amount, "USD")} the client owed is written off.",
                _ => $" The {MoneyText.Amount(amount, "USD")} paid ahead is part of its price now.",
            }
            : "";
        return new ProtonActionDone($"{project.Title} is now {ProjectText.Status(project.Status).ToLowerInvariant()}.{settled}",
            "project", project.Id, [ProjectText.Ref(project)], ProtonUiCommand.Open(ProjectText.Ref(project)));
    }
}
