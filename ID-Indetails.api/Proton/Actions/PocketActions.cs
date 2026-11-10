using System.ComponentModel;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Execution.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/// <param name="Before">The site's profit when proposed: if it changed since, the move is refused (a round-off would no longer round).</param>
public sealed record PocketContributionArgs(PocketContributionRequest Request, string Title, decimal Before);

public sealed record PocketContributionInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("USD, as the user gave it. Leave it out with round_off.")]
    public decimal? Amount { get; init; }

    [Description("Move what is over the site's last whole thousand of profit, so it is left with a round number ($243,459 leaves $243,000 by moving $459).")]
    public bool RoundOff { get; init; }

    public string? Note { get; init; }
}

/// <summary>
/// Moves part of an Execution client site's money into the pocket money (POST /api/execution/pocket/contributions) once
/// confirmed: an amount the user typed, or the round-off that leaves the site's profit a whole thousand. Either way the
/// user types the amount on the card. It lowers the site's profit, never what the client owes.
/// </summary>
public sealed class ProposePocketContributionTool(IProjectService projects, IPocketService pocket)
    : ProtonActionTool<PocketContributionInput, PocketContributionArgs>
{
    public override string Name => "propose_pocket_contribution";

    public override string Description =>
        "Propose moving part of an Execution client site's money into the pocket money: an amount the user gave, or round_off, " +
        "which moves what is over the site's last whole thousand of profit so it is left with a round number. It lowers the " +
        "site's profit, never what the client owes, and never takes the profit to zero. The user confirms by typing the amount.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<PocketController>(nameof(PocketController.Contribute));

    protected override async Task<(PocketContributionArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        PocketContributionInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.RoundOff == input.Amount.HasValue)
        {
            throw new ProtonToolInputException("Give either the amount the user named or round_off, not both.");
        }

        MoneyText.RequireTyped(context, input.Amount);
        var site = await projects.GetAsync(input.ProjectId, cancellationToken);
        if (site.Domain != DomainCodes.Execution || site.BillingType != BillingType.Client)
        {
            throw new ProtonToolInputException("Only an Execution client site moves money into the pocket money.");
        }

        var money = site.Money ?? throw new ProtonToolInputException("The user can't see this site's money.");
        var amount = input.Amount ?? PocketRules.RoundOff(money.Profit);
        if (amount <= 0)
        {
            throw new ProtonToolInputException(input.RoundOff
                ? money.Profit < PocketRules.RoundTo
                    ? $"The site's profit is {MoneyText.Amount(money.Profit, money.Currency)}, under a whole thousand: there is nothing to round off."
                    : $"The site's profit is already a round {MoneyText.Amount(money.Profit, money.Currency)}."
                : "The amount must be more than zero.");
        }

        if (PocketRules.CheckContribution(amount, money.Profit) is { } denial)
        {
            throw new ProtonToolInputException(denial.Message);
        }

        var request = new PocketContributionRequest { ProjectId = site.Id, Amount = amount, Note = MoneyText.Clean(input.Note) };
        var changes = new List<ProtonChange>
        {
            new("Site profit", MoneyText.Amount(money.Profit, money.Currency), MoneyText.Amount(money.Profit - amount, money.Currency)),
        };
        if (request.Note is { } note)
        {
            changes.Add(new ProtonChange("Note", null, note));
        }

        return (new PocketContributionArgs(request, site.Title, money.Profit),
            new ProtonPreview($"Move {MoneyText.Amount(amount, money.Currency)} from {site.Title} to the pocket money", changes,
                (input.RoundOff ? "It leaves the site's profit a whole thousand. " : "") + "What the client owes doesn't change.",
                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(amount)),
            ProjectText.Ref(site));
    }

    protected override object? RequestOf(PocketContributionArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(PocketContributionArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var site = await projects.GetAsync(args.Request.ProjectId, cancellationToken);
        if (site.Money is null || site.Money.Profit != args.Before)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The site's profit changed since Proton proposed this. Ask again.");
        }

        await pocket.ContributeAsync(args.Request, cancellationToken);
        return new ProtonActionDone($"Moved {MoneyText.Amount(args.Request.Amount, "USD")} from {args.Title} to the pocket money.", "project", site.Id, [ProjectText.Ref(site)]);
    }
}
