using System.ComponentModel;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Boqs.Abstraction;
using ID_Indetails.api.Services.Clients.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

/// <param name="SupervisionBefore">The rates when the card was made: if they changed since, the card is stale.</param>
public sealed record SitePricingArgs(long ProjectId, string ProjectTitle, UpdateProjectPricingRequest Request, decimal SupervisionBefore, decimal TvaBefore);

public sealed record SitePricingInput : ProtonActionInput
{
    [Description("An Execution site.")]
    public required long ProjectId { get; init; }

    [Description("The new supervision rate as a percentage, exactly as the user said it (10 for 10%, 12.5 for 12.5%). Leave out to keep it.")]
    public decimal? SupervisionPercent { get; init; }

    [Description("The new TVA rate as a percentage, exactly as the user said it (11 for 11%). Leave out to keep it.")]
    public decimal? TvaPercent { get; init; }

    [Description("Why the rates change, as the user gave it (it goes in the site's payment history). Ask for it if they didn't say.")]
    public required string Reason { get; init; }
}

/// <summary>
/// Changes an Execution site's supervision rate or TVA, through PUT /api/projects/{id}/pricing once confirmed. The work price
/// is never typed: it is the agreed BOQs. Both rates must be ones the user typed, and the user types the site's name to confirm.
/// A lower supervision is held to the profit floor by the service at Confirm.
/// </summary>
public sealed class ProposeSitePricingTool(IProjectService projects, IBoqService boqs) : ProtonActionTool<SitePricingInput, SitePricingArgs>
{
    public override string Name => "propose_site_pricing";

    public override string Description =>
        "Propose changing an Execution site's supervision rate or TVA rate, with why. The site's work price can't be set here: it is the " +
        "agreed BOQs (propose_boq_agreement). Rates must be percentages the user typed. The card shows supervision, TVA and what the " +
        "client pays before and after. The user types the site's name to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Execution;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.UpdatePricing));

    protected override async Task<(SitePricingArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        SitePricingInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.SupervisionPercent is null && input.TvaPercent is null)
        {
            throw new ProtonToolInputException("Say which rate changes: supervision_percent, tva_percent or both.");
        }

        MoneyText.RequireTyped(context, input.SupervisionPercent, input.TvaPercent);
        var reason = MoneyText.Clean(input.Reason) ?? throw new ProtonToolInputException("Ask the user why the rates change: it is required.");
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        if (project.Domain != DomainCodes.Execution)
        {
            throw new ProtonToolInputException("Only an Execution site has a supervision rate. A Design project's revenue is propose_project_money.");
        }

        ProjectRules.CheckMoneyEditable(project.FinalizedAt is not null).ThrowIfDenied();
        var price = (await boqs.ListAsync(project.Id, cancellationToken)).Price
            ?? throw new ProtonToolInputException("The user can't see this site's pricing.");
        var supervision = input.SupervisionPercent is { } s ? s / 100m : price.SupervisionRate;
        var tva = input.TvaPercent is { } t ? t / 100m : price.TvaRate;
        ProjectRules.CheckPaymentChanges(supervision != price.SupervisionRate || tva != price.TvaRate, "supervisionRate").ThrowIfDenied();

        var after = ExecutionPricing.PriceOf(price.WorkUsd, supervision, tva);
        var changes = new List<ProtonChange>();
        if (supervision != price.SupervisionRate)
        {
            changes.Add(new ProtonChange("Supervision rate", BoqText.Percent(price.SupervisionRate), BoqText.Percent(supervision)));
            changes.Add(new ProtonChange("Supervision", BoqText.Usd(price.SupervisionUsd), BoqText.Usd(after.Supervision)));
        }

        if (tva != price.TvaRate)
        {
            changes.Add(new ProtonChange("TVA rate", BoqText.Percent(price.TvaRate), BoqText.Percent(tva)));
            changes.Add(new ProtonChange("TVA (on the work only)", BoqText.Usd(price.TvaUsd), BoqText.Usd(after.Tva)));
        }

        changes.Add(new ProtonChange("The client pays", BoqText.Usd(price.ClientTotalUsd), BoqText.Usd(after.Gross)));
        changes.Add(new ProtonChange("Why", null, reason));

        var note = "Every BOQ and what the client pays follow the new rates from now on. The work price stays " + BoqText.Usd(price.WorkUsd) + ". "
            + (supervision != price.SupervisionRate ? "Supervision is part of the site's revenue, so it moves the revenue and the profit; TVA does not. " : "TVA is not part of the revenue or the profit. ")
            + (supervision < price.SupervisionRate ? "If a lower supervision would take the profit to zero or below, it is refused and kept for someone who sees the site's money to force through." : "");
        var request = new UpdateProjectPricingRequest
        {
            SupervisionRate = input.SupervisionPercent is null ? null : supervision,
            TvaRate = input.TvaPercent is null ? null : tva,
            Description = reason,
        };
        return (new SitePricingArgs(project.Id, project.Title, request, price.SupervisionRate, price.TvaRate),
            new ProtonPreview($"Change the pricing of {project.Title}", changes, note.Trim(), Confirm: ProtonConfirm.Typed, Phrase: project.Title),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(SitePricingArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(SitePricingArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        // the card showed the rates as they were; changed since, the figures the user read are not what would be made
        var current = (await boqs.ListAsync(args.ProjectId, cancellationToken)).Price;
        if (current is null || current.SupervisionRate != args.SupervisionBefore || current.TvaRate != args.TvaBefore)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The site's rates changed since Proton proposed this. Ask again.");
        }

        var project = await projects.UpdatePricingAsync(args.ProjectId, args.Request, cancellationToken);
        return new ProtonActionDone($"Changed the pricing of {project.Title}.", "project", project.Id, [ProjectText.Ref(project)],
            ProtonUiCommand.Open(ProjectText.Ref(project), ProtonProjectSection.Financials, ProtonProjectPart.Boqs));
    }
}

/// <param name="BillingBefore">The project's billing when the card was made: if it changed since, the card is stale.</param>
public sealed record ProjectBillingArgs(
    long ProjectId, string ProjectTitle, UpdateProjectBillingRequest Request, BillingType BillingBefore, long? ClientBefore, decimal RevenueBefore);

public sealed record ProjectBillingInput : ProtonActionInput
{
    [Description("A Design project.")]
    public required long ProjectId { get; init; }

    [Description("client: client work, which needs a client. internal: the company's own work, with no client and no payment: its agreed revenue becomes 0.")]
    public required BillingType BillingType { get; init; }

    [Description("For client work, the client it is for (id from find). Leave out for internal work.")]
    public long? ClientId { get; init; }
}

/// <summary>
/// Makes a Design project client work or internal, or changes its client, through PUT /api/projects/{id}/billing once
/// confirmed. Becoming internal drops the agreed revenue to 0, and a new client ends the previous client's portal access, so
/// the user types the project's name; a project with invoices or payments can't change client (the service refuses).
/// </summary>
public sealed class ProposeProjectBillingTool(IProjectService projects, IClientService clients) : ProtonActionTool<ProjectBillingInput, ProjectBillingArgs>
{
    public override string Name => "propose_project_billing";

    public override string Description =>
        "Propose making a Design project client work or internal, or changing the client it is for. Internal work has no client and no " +
        "payment (its agreed revenue becomes 0); a project that has invoices or payments can't change client. An Execution site is always " +
        "client work. The user types the project's name to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<ProjectsController>(nameof(ProjectsController.UpdateBilling));

    protected override async Task<(ProjectBillingArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        ProjectBillingInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        if (project.Domain == DomainCodes.Execution)
        {
            throw new ProtonToolInputException("An Execution site is always client work; its client is fixed and its rates are propose_site_pricing.");
        }

        var money = project.Money ?? throw new ProtonToolInputException("The user can't see this project's money.");
        ProjectRules.CheckMoneyEditable(money.IsLocked).ThrowIfDenied();
        var revenue = input.BillingType == BillingType.Internal ? 0 : money.Revenue;
        ProjectRules.CheckSetup(new ProjectRules.Setup(project.Domain, input.BillingType, input.ClientId, revenue, null)).ThrowIfDenied();
        if (input.BillingType == project.BillingType && input.ClientId == project.Client?.Id)
        {
            throw new ProtonToolInputException("Nothing would change: the project is already set up like that.");
        }

        // a project with invoices or payments keeps the client they were made for (the service checks its own records too)
        var billing = money.Billing;
        ProjectRules.CheckClientChange(project.Client?.Id, input.ClientId, billing.IssuedCount > 0 || billing.PaymentCount > 0 || billing.Drafts > 0).ThrowIfDenied();

        var clientName = "none";
        if (input.ClientId is { } clientId)
        {
            var client = await clients.GetAsync(clientId, cancellationToken);
            if (client.Domain != project.Domain)
            {
                throw new RequestValidationException(ErrorCodes.ValidationFailed, "clientId", "The client does not exist in this domain.");
            }

            ProjectRules.CheckClient(client.IsArchived).ThrowIfDenied();
            clientName = client.DisplayName;
        }

        var changes = new List<ProtonChange>();
        SettingsText.Compare(changes, "Billing", Billing(project.BillingType), Billing(input.BillingType));
        SettingsText.Compare(changes, "Client", project.Client?.DisplayName ?? "none", clientName);
        var drops = input.BillingType == BillingType.Internal && money.Revenue > 0;
        if (drops)
        {
            changes.Add(new ProtonChange("Agreed revenue", MoneyText.Amount(money.Revenue, money.Currency), MoneyText.Amount(0, money.Currency)));
            changes.Add(new ProtonChange("TVA", BoqText.Percent(money.TvaRate), BoqText.Percent(0)));
        }

        var losesPortal = project.Client is not null && input.ClientId != project.Client.Id;
        var notes = new List<string>();
        if (drops)
        {
            notes.Add($"Internal work has no payment: the agreed revenue of {MoneyText.Amount(money.Revenue, money.Currency)} stays in the payment history only.");
        }
        else if (input.BillingType == BillingType.Client && project.BillingType == BillingType.Internal)
        {
            notes.Add("Its agreed revenue stays at 0 until it is set (propose_project_money).");
        }

        if (losesPortal)
        {
            notes.Add($"{project.Client!.DisplayName}'s contacts lose their access to this project in the client portal, and what was published for them is withdrawn.");
        }

        var request = new UpdateProjectBillingRequest { BillingType = input.BillingType, ClientId = input.ClientId };
        return (new ProjectBillingArgs(project.Id, project.Title, request, project.BillingType, project.Client?.Id, money.Revenue),
            new ProtonPreview($"Change how {project.Title} is billed", changes, notes.Count == 0 ? null : string.Join(" ", notes),
                Confirm: ProtonConfirm.Typed, Phrase: project.Title, Irreversible: drops || losesPortal),
            ProjectText.Ref(project));
    }

    protected override object? RequestOf(ProjectBillingArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(ProjectBillingArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await projects.GetAsync(args.ProjectId, cancellationToken);
        if (current.BillingType != args.BillingBefore || current.Client?.Id != args.ClientBefore || current.Money?.Revenue != args.RevenueBefore)
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The project's billing changed since Proton proposed this. Ask again.");
        }

        var project = await projects.UpdateBillingAsync(args.ProjectId, args.Request, cancellationToken);
        return new ProtonActionDone($"Changed how {project.Title} is billed.", "project", project.Id, [ProjectText.Ref(project)], ProtonUiCommand.Open(ProjectText.Ref(project)));
    }

    private static string Billing(BillingType type) => type == BillingType.Internal ? "Internal work" : "Client work";
}
