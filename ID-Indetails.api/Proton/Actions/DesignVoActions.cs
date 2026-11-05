using System.ComponentModel;
using System.Globalization;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Proton.Ui;
using ID_Indetails.api.Services.Documents.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public enum ProtonDesignVoStep
{
    Create,
    Confirm,
    Cancel,
}

/// <param name="VoTotal">The VO as the card read it (confirm, cancel): if its status or total changed since, the card is stale.</param>
/// <param name="PriceBefore">The project's price when the card was made: if another VO or payment moved it, the card is stale.</param>
public sealed record DesignVoStepArgs(
    ProtonDesignVoStep Step, long ProjectId, string ProjectTitle, long? VoId, decimal? VoTotal, decimal PriceBefore, CreateDesignVoRequest? Create);

public sealed record NewDesignVoLineInput
{
    [Description("What the line is, as the user said it.")]
    public required string Description { get; init; }

    [Description("USD before TVA, exactly as the user gave it: above zero for more work, below zero for work taken out.")]
    public required decimal Amount { get; init; }
}

public sealed record DesignVoAllocationInput
{
    [Description("One of the project's departments, from list_design_vos.")]
    public required long DepartmentId { get; init; }

    [Description("Its part of the total, in USD, exactly as the user gave it.")]
    public required decimal Amount { get; init; }
}

public sealed record DesignVoStepInput : ProtonActionInput
{
    [Description("create a variation order (applied now, or pending until the client confirms it); confirm a pending one (the client agreed: it joins the price); cancel a pending one.")]
    public required ProtonDesignVoStep Step { get; init; }

    [Description("For create: the Design client project (from find or list_design_vos).")]
    public long? ProjectId { get; init; }

    [Description("For confirm and cancel: from list_design_vos.")]
    public long? VoId { get; init; }

    [Description("For create: its title, as the user said it.")]
    public string? Title { get; init; }

    [Description("For create: the lines, each described, with the amount the user gave. The total is worked out and can't be zero.")]
    public IReadOnlyList<NewDesignVoLineInput>? Lines { get; init; }

    [Description("For create: shares (the default) divides the total by the project's department shares; departments gives each department an amount.")]
    public DesignVoSplit? Split { get; init; }

    [Description("For create with split departments: each department's amount, adding up to the total.")]
    public IReadOnlyList<DesignVoAllocationInput>? Allocations { get; init; }

    [Description("For create: show on the VO the hours logged outside the agreed scope since the last applied VO (see list_design_vos). Default no.")]
    public bool? ShowOutsideScope { get; init; }

    [Description("For create: true joins the price at once; false (the default) leaves it pending for the client to confirm. Only when the user said which.")]
    public bool? Apply { get; init; }
}

/// <summary>
/// Makes a Design variation order, confirms a pending one or takes it back, through the VO endpoints once confirmed. Every
/// amount is one the user typed and the user types the project's name to confirm. Applying one that lowers the price is held to
/// the profit floor by the service: refused, it is kept as a blocked change for someone who sees the money to force.
/// </summary>
public sealed class ProposeDesignVoTool(
    IProjectService projects, IDesignVoService vos, IProjectDepartmentService departments, IBlockedChangeRecorder recorder)
    : ProtonActionTool<DesignVoStepInput, DesignVoStepArgs>
{
    public override string Name => "propose_design_vo";

    public override string Description =>
        "Propose a variation order (VO) on a Design client project: making one from lines of work added (above zero) or taken out (below), " +
        "divided between the departments by their shares or by amounts; applied to the price at once or left pending for the client; " +
        "confirming a pending VO the client agreed to; or cancelling a pending one. Amounts must be ones the user typed. The card shows " +
        "what the price becomes. Emailing the VO to the client is propose_send_design_vo. The user types the project's name to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Design;

    // Create, Confirm and Cancel share their policies (financial:write).
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<DesignVosController>(nameof(DesignVosController.Create));

    protected override Task<(DesignVoStepArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        DesignVoStepInput input, ProtonToolContext context, CancellationToken cancellationToken)
        => input.Step == ProtonDesignVoStep.Create
            ? CreateAsync(input, context, cancellationToken)
            : DecideAsync(input, cancellationToken);

    private async Task<(DesignVoStepArgs, ProtonPreview, ProtonRef?)> CreateAsync(
        DesignVoStepInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var lines = (input.Lines ?? []).Select(l => new DesignVoLineRequest(l.Description.Trim(), l.Amount)).ToList();
        var allocations = (input.Allocations ?? []).Select(a => new DesignVoAllocationRequest(a.DepartmentId, a.Amount)).ToList();
        // a line below zero takes work out: the user says "1,000" for it, and the sign is the line's
        MoneyText.RequireTyped(context,
            [.. lines.Select(l => Math.Abs(l.Amount)).Concat(allocations.Select(a => Math.Abs(a.Amount))).Where(amount => amount != 0).Select(amount => (decimal?)amount)]);

        var project = await DesignVoText.OpenProjectAsync(projects, input.ProjectId ?? throw new ProtonToolInputException("Give the project (project_id)."), cancellationToken);
        var price = project.Money!.Revenue;
        var split = input.Split ?? DesignVoSplit.Shares;
        var title = MoneyText.Clean(input.Title) ?? throw new ProtonToolInputException("Give the variation order a title.");
        var rules = lines.Select(l => new DesignVoRules.Line(l.Description, l.Amount)).ToList();
        DesignVoRules.CheckLines(rules).ThrowIfDenied();
        var total = DesignVoRules.Total(rules);

        var shares = (await departments.GetAsync(project.Id, cancellationToken)).Departments;
        DesignVoRules.CheckAllocations(split, [.. shares.Select(d => d.Id)], [.. allocations.Select(a => (a.DepartmentId, a.Amount))], total).ThrowIfDenied();
        DesignVoRules.CheckPrice(price, total).ThrowIfDenied();

        var apply = input.Apply ?? false;
        var request = new CreateDesignVoRequest
        {
            Title = title, Lines = lines, Split = split, Allocations = allocations, ShowOutsideScope = input.ShowOutsideScope ?? false, Apply = apply,
        };

        var changes = new List<ProtonChange> { new("Title", null, title) };
        changes.AddRange(lines.Select((l, i) => new ProtonChange($"Line {i + 1}", null, $"{ProtonText.Cut(l.Description, 80)}: {DesignVoText.Usd(l.Amount)}")));
        changes.AddRange(DesignVoText.TotalRows(total, project.Money.TvaRate));
        changes.AddRange(split == DesignVoSplit.Shares
            ? DesignVoRules.ByShares(total, [.. shares.Select(d => (d.Id, d.Share ?? 0))]).Select(a => DesignVoText.Part(shares, a.TeamId, a.Amount, " (by its share)"))
            : allocations.Select(a => DesignVoText.Part(shares, a.DepartmentId, a.Amount, "")));
        if (request.ShowOutsideScope)
        {
            changes.Add(new ProtonChange("Shows", null, "the hours logged outside the agreed scope since the last applied VO"));
        }

        changes.Add(apply ? DesignVoText.PriceRow(price, total, project.Money.TvaRate) : new ProtonChange("Status", null, "Pending: the price stays " + DesignVoText.Usd(price) + " until the client confirms"));
        var note = apply
            ? "It joins the price at once and goes in the project's payment history."
              + (total < 0 ? " It lowers the price: if that would take the project's profit to zero or below, it is refused by the profit floor, the VO stays pending, and the refusal is kept for someone with financial:view to force." : "")
            : "It is made as sent to the client for their approval, but nothing is emailed: that is propose_send_design_vo. The price changes when it is confirmed.";
        return (new DesignVoStepArgs(ProtonDesignVoStep.Create, project.Id, project.Title, null, null, price, request),
            new ProtonPreview($"{(apply ? "Apply" : "Make")} a variation order on {project.Title}", changes, note, Confirm: ProtonConfirm.Typed, Phrase: project.Title),
            ProjectText.Ref(project));
    }

    private async Task<(DesignVoStepArgs, ProtonPreview, ProtonRef?)> DecideAsync(DesignVoStepInput input, CancellationToken cancellationToken)
    {
        var document = await vos.GetDocumentAsync(input.VoId ?? throw new ProtonToolInputException("Give the variation order (vo_id) from list_design_vos."), cancellationToken);
        var vo = document.Vo;
        var project = await DesignVoText.OpenProjectAsync(projects, vo.ProjectId, cancellationToken);
        var price = project.Money!.Revenue;
        DesignVoRules.CheckPending(vo.Status).ThrowIfDenied();
        var name = DesignVoText.Name(vo);
        var target = ProjectText.Ref(project);
        var args = new DesignVoStepArgs(input.Step, project.Id, project.Title, vo.Id, vo.TotalUsd, price, null);

        if (input.Step == ProtonDesignVoStep.Cancel)
        {
            return (args,
                new ProtonPreview($"Cancel {name}",
                    [new ProtonChange("Variation order", $"Pending, {DesignVoText.Usd(vo.TotalUsd)}", "Cancelled"),
                        new ProtonChange("Price", DesignVoText.Usd(price), DesignVoText.Usd(price) + " (unchanged)")],
                    "Its number isn't used again. Anything already emailed to the client stays sent.",
                    Confirm: ProtonConfirm.Typed, Phrase: project.Title, Irreversible: true),
                target);
        }

        DesignVoRules.CheckPrice(price, vo.TotalUsd).ThrowIfDenied();
        var changes = new List<ProtonChange> { new("Variation order", "Pending", "Applied") };
        changes.AddRange(DesignVoText.TotalRows(vo.TotalUsd, vo.TvaRate));
        changes.Add(DesignVoText.PriceRow(price, vo.TotalUsd, project.Money.TvaRate));
        if (vo.Split == DesignVoSplit.Shares)
        {
            var shares = (await departments.GetAsync(project.Id, cancellationToken)).Departments;
            changes.AddRange(DesignVoRules.ByShares(vo.TotalUsd, [.. shares.Select(d => (d.Id, d.Share ?? 0))])
                .Select(a => DesignVoText.Part(shares, a.TeamId, a.Amount, " (by its share now)")));
        }
        else
        {
            changes.AddRange(vo.Allocations.Select(a => new ProtonChange($"{a.Name} gets", null, DesignVoText.Usd(a.Amount))));
        }

        return (args,
            new ProtonPreview($"Confirm {name}", changes,
                "The client agreed: it joins the price and goes in the project's payment history."
                + (vo.TotalUsd < 0 ? " It lowers the price: if that would take the project's profit to zero or below, it is refused and kept for someone with financial:view to force." : ""),
                Confirm: ProtonConfirm.Typed, Phrase: project.Title),
            target);
    }

    protected override object? RequestOf(DesignVoStepArgs args) => args.Create;

    protected override async Task<ProtonActionDone> ExecuteAsync(DesignVoStepArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        // the card showed the VO and the price as they were: the user typed the project's name having read them
        var project = await projects.GetAsync(args.ProjectId, cancellationToken);
        if (project.Money?.Revenue != args.PriceBefore)
        {
            throw DesignVoText.Stale("The project's price changed since Proton proposed this. Ask again.");
        }

        if (args.Step != ProtonDesignVoStep.Create)
        {
            var current = (await vos.GetDocumentAsync(args.VoId!.Value, cancellationToken)).Vo;
            if (current.Status != DesignVoStatus.Pending || current.TotalUsd != args.VoTotal)
            {
                throw DesignVoText.Stale("The variation order changed since Proton proposed this. Ask again.");
            }
        }

        var target = ProjectText.Ref(project);
        var open = ProtonUiCommand.Open(target, ProtonProjectSection.Financials, ProtonProjectPart.Payments);
        try
        {
            switch (args.Step)
            {
                case ProtonDesignVoStep.Create:
                    var made = await vos.CreateAsync(args.ProjectId, args.Create!, cancellationToken);
                    return new ProtonActionDone(
                        made.Status == DesignVoStatus.Applied
                            ? $"Applied {DesignVoText.Name(made)}. The price is now {DesignVoText.Usd(args.PriceBefore + made.TotalUsd)}."
                            : $"Made {DesignVoText.Name(made)}, pending until the client confirms it. The price stays {DesignVoText.Usd(args.PriceBefore)}.",
                        "design_vo", made.Id, [target], open);
                case ProtonDesignVoStep.Confirm:
                    var confirmed = await vos.ConfirmAsync(args.VoId!.Value, cancellationToken);
                    return new ProtonActionDone($"Confirmed {DesignVoText.Name(confirmed)}. The price is now {DesignVoText.Usd(args.PriceBefore + confirmed.TotalUsd)}.",
                        "design_vo", confirmed.Id, [target], open);
                default:
                    var cancelled = await vos.CancelAsync(args.VoId!.Value, cancellationToken);
                    return new ProtonActionDone($"Cancelled {DesignVoText.Name(cancelled)}.", "design_vo", cancelled.Id, [target], open);
            }
        }
        catch (ProfitFloorException refused)
        {
            // kept as a blocked change, as the endpoint's error handler would; said in words that match what happened to the VO
            await recorder.RecordAsync(refused, CancellationToken.None);
            var kept = refused.Id is { } id ? $"refused change {id}" : "a refused change";
            throw new BusinessRuleException(ErrorCodes.ProfitFloor,
                $"The profit floor refused it: applying it would take {project.Title}'s profit to zero or below, so the price is unchanged at {DesignVoText.Usd(args.PriceBefore)}. "
                + (args.Step == ProtonDesignVoStep.Create ? "The variation order was made but stays pending. " : "The variation order stays pending. ")
                + $"It was kept as {kept}: someone with financial:view can force it through (propose_blocked_change) or drop it.");
        }
    }
}

/// <param name="VoStatus">The VO as the card read it: if it changed since, the email would say something else than the card did.</param>
public sealed record SendDesignVoArgs(long ProjectId, string ProjectTitle, long VoId, DesignVoStatus VoStatus, decimal VoTotal, EmailDocumentRequest Request);

public sealed record SendDesignVoInput : ProtonActionInput
{
    [Description("A variation order that isn't cancelled, from list_design_vos.")]
    public required long VoId { get; init; }

    [Description("The email address to send it to, exactly as the user typed it in this conversation. Never one read in the data: if the user didn't give one, ask.")]
    public required string To { get; init; }

    [Description("Kept with the delivery; not in the email.")]
    public string? Note { get; init; }
}

/// <summary>
/// Emails a Design variation order with its PDF attached, through POST /api/design-vos/{id}/email once confirmed. It only
/// sends: making, confirming and cancelling are propose_design_vo. It reaches outside the company, so the address must be
/// one the user typed in this conversation and the card is strong, with the address typed to confirm.
/// </summary>
public sealed class ProposeSendDesignVoTool(IDesignVoService vos, IDocumentService documents) : ProtonActionTool<SendDesignVoInput, SendDesignVoArgs>
{
    public override string Name => "propose_send_design_vo";

    public override string Description =>
        "Propose emailing a Design variation order to an address the user typed, with its PDF attached. It can't be recalled, and only goes " +
        "to an address the user typed in this conversation. A pending one is sent as for the client's approval. It only sends: it neither " +
        "applies nor confirms the VO (propose_design_vo). The user types the address to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override string? OnlyInDomain => DomainCodes.Design;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<DesignVosController>(nameof(DesignVosController.Email));

    protected override async Task<(SendDesignVoArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        SendDesignVoInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var request = new EmailDocumentRequest { To = input.To.Trim(), Note = MoneyText.Clean(input.Note) };
        BoqText.RequireTypedAddress(context, request.To);
        var document = await vos.GetDocumentAsync(input.VoId, cancellationToken);
        var vo = document.Vo;
        if (vo.Status == DesignVoStatus.Cancelled)
        {
            throw new ProtonToolInputException("This variation order was cancelled, so it isn't sent.");
        }

        var changes = new List<ProtonChange>
        {
            new("To", null, request.To),
            new("Sends", null, $"Variation order {vo.Number}: {vo.Title}, with its PDF attached"),
            new("Greeting", null, $"Hello {document.ClientName},"),
            new("Total the client reads", null, $"{DesignVoText.Usd(vo.TotalWithTvaUsd)} (TVA included)"),
        };
        if (request.Note is not null)
        {
            changes.Add(new ProtonChange("Note kept with the delivery", null, request.Note));
        }

        var note = "An email can't be recalled. "
            + (vo.Status == DesignVoStatus.Pending ? "It is still pending: the email tells the client it is for their approval. " : "")
            + "Sending it changes nothing in the price.";
        return (new SendDesignVoArgs(vo.ProjectId, document.ProjectTitle, vo.Id, vo.Status, vo.TotalUsd, request),
            new ProtonPreview($"Email {DesignVoText.Name(vo)} to {request.To}", changes, note,
                Confirm: ProtonConfirm.Typed, Phrase: request.To, Irreversible: true, Reach: "1 person outside the company"),
            new ProtonRef(ProtonRefType.Project, vo.ProjectId, document.ProjectTitle, DomainCodes.Design));
    }

    protected override object? RequestOf(SendDesignVoArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(SendDesignVoArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = (await vos.GetDocumentAsync(args.VoId, cancellationToken)).Vo;
        if (current.Status != args.VoStatus || current.TotalUsd != args.VoTotal)
        {
            throw DesignVoText.Stale("The variation order changed since Proton proposed this. Ask again.");
        }

        await documents.EmailDesignVoAsync(args.VoId, args.Request, cancellationToken);
        var project = new ProtonRef(ProtonRefType.Project, args.ProjectId, args.ProjectTitle, DomainCodes.Design);
        return new ProtonActionDone($"Emailed {DesignVoText.Name(current)} to {args.Request.To}.", "design_vo", args.VoId, [project]);
    }
}

/// <summary>What the Design VO cards share: reading the project as the endpoints do and the wording of amounts.</summary>
internal static class DesignVoText
{
    /// <summary>A Design client project the user sees the money of and that isn't finalised, as the VO endpoints read it.</summary>
    public static async Task<ProjectResponse> OpenProjectAsync(IProjectService projects, long projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project.Domain != DomainCodes.Design)
        {
            throw new ProtonToolInputException("Execution sites change their price with BOQ variation orders (propose_boq).");
        }

        ProjectRules.CheckHasPayment(project.BillingType).ThrowIfDenied();
        var money = project.Money ?? throw new ProtonToolInputException("The user can't see this project's money.");
        ProjectRules.CheckMoneyEditable(money.IsLocked).ThrowIfDenied();
        return project;
    }

    public static string Usd(decimal amount) => MoneyText.Amount(amount, "USD");

    public static string Name(DesignVoResponse vo) => $"{vo.Number} ({vo.Title})";

    public static IEnumerable<ProtonChange> TotalRows(decimal total, decimal tvaRate)
    {
        var tva = Math.Round(total * tvaRate, 2, MidpointRounding.AwayFromZero);
        return
        [
            new ProtonChange("Total (before TVA)", null, Usd(total)),
            new ProtonChange($"TVA ({(tvaRate * 100).ToString("0.##", CultureInfo.InvariantCulture)}%)", null, Usd(tva)),
            new ProtonChange("Total with TVA", null, Usd(total + tva)),
        ];
    }

    /// <summary>What the project's price (before TVA, and with it) becomes when <paramref name="total"/> joins it.</summary>
    public static ProtonChange PriceRow(decimal price, decimal total, decimal tvaRate)
    {
        decimal WithTva(decimal net) => net + Math.Round(net * tvaRate, 2, MidpointRounding.AwayFromZero);
        return new ProtonChange("The price becomes",
            $"{Usd(price)} ({Usd(WithTva(price))} with TVA)", $"{Usd(price + total)} ({Usd(WithTva(price + total))} with TVA)");
    }

    public static ProtonChange Part(IReadOnlyList<ProjectDepartmentResponse> departments, long departmentId, decimal amount, string how)
        => new($"{departments.FirstOrDefault(d => d.Id == departmentId)?.Name ?? "Department"} gets", null, Usd(amount) + how);

    public static BusinessRuleException Stale(string message) => new(ErrorCodes.ProtonActionStale, message);
}
