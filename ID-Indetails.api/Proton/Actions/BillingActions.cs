using System.ComponentModel;
using System.Globalization;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Controllers;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Tools;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Proton.Actions;

public sealed record InvoiceLineInput
{
    public required string Description { get; init; }

    [Description("The line's amount in the invoice's currency, as the user gave it.")]
    public required decimal Amount { get; init; }
}

/// <param name="Before">The draft as it was when proposed, for an edit: if it changed since, the edit is refused.</param>
public sealed record InvoiceDraftArgs(long? ProjectId, long? InvoiceId, SaveInvoiceRequest Request, SaveInvoiceRequest? Before);

public sealed record CreateInvoiceInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("The lines, exactly as the user gave them. Never made up.")]
    public required IReadOnlyList<InvoiceLineInput> Lines { get; init; }

    [Description("Days the client has to pay; the domain's default when not given.")]
    public int? TermsDays { get; init; }

    [Description("Printed on the invoice for the client.")]
    public string? Notes { get; init; }
}

/// <summary>
/// Makes a draft invoice for a client project, through POST /api/projects/{id}/invoices once confirmed. A draft is
/// sent to nobody and counts for nothing until it is issued.
/// </summary>
public sealed class ProposeCreateInvoiceTool(IProjectService projects, IInvoiceService invoices) : ProtonActionTool<CreateInvoiceInput, InvoiceDraftArgs>
{
    public override string Name => "propose_create_invoice";

    public override string Description =>
        "Propose a draft invoice for a client project with the lines the user gave. A draft is sent to nobody until " +
        "it is issued. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    protected override bool OpensByDefault => true;

    protected override string OpensLabel(InvoiceDraftArgs args, ProtonRef? target) => "the project's invoices";

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<InvoicesController>(nameof(InvoicesController.Create));

    protected override async Task<(InvoiceDraftArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        CreateInvoiceInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        InvoiceRules.CheckBillable(project.BillingType, project.Client?.Id).ThrowIfDenied();
        var request = new SaveInvoiceRequest
        {
            Lines = MoneyText.LinesOf(input.Lines), TermsDays = input.TermsDays, Notes = MoneyText.Clean(input.Notes),
        };
        // Every field the draft gets is on the card, and text the model wrote is only ever a value, never a row's label.
        var changes = new List<ProtonChange>
        {
            new("Project", null, project.Title),
            new("Bill to", null, project.Client?.DisplayName ?? "the client"),
            new("Lines", null, MoneyText.Lines(request.Lines, null)),
            new("Total", null, MoneyText.Amount(request.Lines.Sum(l => l.Amount), null)),
        };
        if (request.TermsDays is { } days)
        {
            changes.Add(new ProtonChange("Terms", null, $"{days} days"));
        }

        if (request.Notes is { } notes)
        {
            changes.Add(new ProtonChange("Notes printed for the client", null, notes));
        }

        return (new InvoiceDraftArgs(project.Id, null, request, null),
            new ProtonPreview($"Draft an invoice for {project.Title}", changes,
                "A draft in the project's currency: nobody sees it until it is issued."),
            ProjectText.Ref(project));
    }

    protected override object RequestOf(InvoiceDraftArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(InvoiceDraftArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var invoice = await invoices.CreateAsync(args.ProjectId!.Value, args.Request, cancellationToken);
        return new ProtonActionDone($"Drafted an invoice of {MoneyText.Amount(invoice.Total, invoice.Currency)} for {invoice.Project.Title}.",
            "invoice", invoice.Id, [MoneyText.ProjectRef(invoice)]);
    }
}

public sealed record UpdateInvoiceInput : ProtonActionInput
{
    [Description("A draft invoice (from list_invoices).")]
    public required long InvoiceId { get; init; }

    [Description("The complete new list of lines, if they change.")]
    public IReadOnlyList<InvoiceLineInput>? Lines { get; init; }

    public int? TermsDays { get; init; }

    [Description("An empty string removes them.")]
    public string? Notes { get; init; }
}

/// <summary>Changes a draft invoice's lines, terms or notes, through PUT /api/invoices/{id} once confirmed.</summary>
public sealed class ProposeUpdateInvoiceTool(IInvoiceService invoices) : ProtonActionTool<UpdateInvoiceInput, InvoiceDraftArgs>
{
    public override string Name => "propose_update_invoice";

    public override string Description =>
        "Propose changing a draft invoice's lines, payment terms or notes. An issued invoice can't be changed: void and " +
        "reissue it. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<InvoicesController>(nameof(InvoicesController.Update));

    protected override async Task<(InvoiceDraftArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        UpdateInvoiceInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetAsync(input.InvoiceId, cancellationToken);
        InvoiceRules.CheckDraft(invoice.Status != InvoiceStatus.Draft, invoice.Status == InvoiceStatus.Void).ThrowIfDenied();
        var before = MoneyText.RequestOf(invoice);
        var after = before with
        {
            Lines = input.Lines is null ? before.Lines : MoneyText.LinesOf(input.Lines),
            TermsDays = input.TermsDays ?? before.TermsDays,
            Notes = input.Notes is null ? before.Notes : MoneyText.Clean(input.Notes),
        };

        var changes = new List<ProtonChange>();
        if (!MoneyText.SameLines(before.Lines, after.Lines))
        {
            changes.Add(new ProtonChange("Lines", MoneyText.Lines(before.Lines, invoice.Currency), MoneyText.Lines(after.Lines, invoice.Currency)));
            changes.Add(new ProtonChange("Total", MoneyText.Amount(invoice.Total, invoice.Currency), MoneyText.Amount(after.Lines.Sum(l => l.Amount), invoice.Currency)));
        }

        if (after.TermsDays != before.TermsDays)
        {
            changes.Add(new ProtonChange("Terms", $"{before.TermsDays} days", $"{after.TermsDays} days"));
        }

        if (after.Notes != before.Notes)
        {
            changes.Add(new ProtonChange("Notes", before.Notes ?? "none", after.Notes ?? "none"));
        }

        if (changes.Count == 0)
        {
            throw new ProtonToolInputException("Nothing would change: say what should be different.");
        }

        return (new InvoiceDraftArgs(null, invoice.Id, after, before), new ProtonPreview($"Change the draft invoice for {invoice.Project.Title}", changes),
            MoneyText.ProjectRef(invoice));
    }

    protected override object RequestOf(InvoiceDraftArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(InvoiceDraftArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var current = await invoices.GetAsync(args.InvoiceId!.Value, cancellationToken);
        if (!MoneyText.Same(MoneyText.RequestOf(current), args.Before!))
        {
            throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The draft changed since Proton proposed this. Ask again.");
        }

        var invoice = await invoices.UpdateAsync(current.Id, args.Request, cancellationToken);
        return new ProtonActionDone($"Updated the draft: {MoneyText.Amount(invoice.Total, invoice.Currency)}.", "invoice", invoice.Id, [MoneyText.ProjectRef(invoice)]);
    }
}

/// <param name="Draft">For issue and discard: the draft as the card showed it. If it changed since, the step is refused.</param>
public sealed record InvoiceStepArgs(
    long InvoiceId, string Step, DateOnly? IssuedOn, string? Reason, RecordDeliveryRequest? Delivery, SaveInvoiceRequest? Draft = null);

public enum ProtonInvoiceStep
{
    Issue,
    Void,
    Discard,
    Unschedule,
}

public sealed record InvoiceStepInput : ProtonActionInput
{
    [Description("From list_invoices.")]
    public required long InvoiceId { get; init; }

    [Description("issue a draft (it gets its number and counts as billed); void an issued one; discard a draft; unschedule a draft set to go out on its own.")]
    public required ProtonInvoiceStep Step { get; init; }

    [Description("For issue: the day it is dated, today or earlier. Default today.")]
    public DateOnly? IssuedOn { get; init; }

    [Description("For void: why, in the user's words. Required.")]
    public string? Reason { get; init; }
}

/// <summary>
/// Issues, voids, discards or unschedules an invoice, through its endpoint once confirmed. Issuing gives it a number
/// and counts it as billed; voiding takes it back; discarding deletes a draft: none of them can be undone, so the user
/// types the total (or, to void, the invoice number) to confirm.
/// </summary>
public sealed class ProposeInvoiceStepTool(IInvoiceService invoices) : ProtonActionTool<InvoiceStepInput, InvoiceStepArgs>
{
    public override string Name => "propose_invoice_step";

    public override string Description =>
        "Propose issuing a draft invoice, voiding an issued one, discarding a draft, or stopping a scheduled draft from " +
        "going out. Issuing, voiding and discarding can't be undone; the user types the total or the invoice number to confirm.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    // Issue, Void, Discard and Unschedule: issue is the widest-reaching; each step's own policy is checked as well
    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<InvoicesController>(nameof(InvoicesController.Issue));

    private static readonly IReadOnlyDictionary<ProtonInvoiceStep, ProtonEndpoint> Steps = new Dictionary<ProtonInvoiceStep, ProtonEndpoint>
    {
        [ProtonInvoiceStep.Issue] = ProtonEndpoint.OfWrite<InvoicesController>(nameof(InvoicesController.Issue)),
        [ProtonInvoiceStep.Void] = ProtonEndpoint.OfWrite<InvoicesController>(nameof(InvoicesController.Void)),
        [ProtonInvoiceStep.Discard] = ProtonEndpoint.OfWrite<InvoicesController>(nameof(InvoicesController.Discard)),
        [ProtonInvoiceStep.Unschedule] = ProtonEndpoint.OfWrite<InvoicesController>(nameof(InvoicesController.Unschedule)),
    };

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints { get; } = Steps.Values.ToList();

    protected override ProtonEndpoint StepOf(InvoiceStepArgs args) => Steps[Enum.Parse<ProtonInvoiceStep>(args.Step, ignoreCase: true)];

    protected override async Task<(InvoiceStepArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        InvoiceStepInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await context.RequireAsync(Steps[input.Step]);
        var invoice = await invoices.GetAsync(input.InvoiceId, cancellationToken);
        var isIssued = invoice.Status is not InvoiceStatus.Draft and not InvoiceStatus.Void;
        var total = MoneyText.Amount(invoice.Total, invoice.Currency);
        var name = invoice.Number ?? $"the draft for {invoice.Project.Title}";
        var draft = MoneyText.RequestOf(invoice);
        ProtonPreview preview;
        switch (input.Step)
        {
            case ProtonInvoiceStep.Issue:
                InvoiceRules.CheckDraft(isIssued, invoice.Status == InvoiceStatus.Void).ThrowIfDenied();
                var issuedOn = input.IssuedOn ?? context.Today;
                InvoiceRules.CheckIssueDate(issuedOn, context.Today).ThrowIfDenied();
                // what goes to the client, in full: the lines and the notes printed on it, not only the total
                List<ProtonChange> issue =
                [
                    new("Status", "Draft", "Issued"),
                    new("Lines", null, MoneyText.Lines(draft.Lines, invoice.Currency)),
                    new("Total", null, total),
                    new("Dated", null, ProjectText.Date(issuedOn)),
                ];
                if (draft.Notes is { } notes)
                {
                    issue.Add(new ProtonChange("Notes printed for the client", null, notes));
                }

                preview = new ProtonPreview($"Issue {name}", issue,
                    $"It gets its number and counts as billed to {invoice.Client.DisplayName}. Sending it is a separate step in the app.",
                    Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(invoice.Total), Irreversible: true);
                return (new InvoiceStepArgs(invoice.Id, "issue", issuedOn, null, null, draft), preview, MoneyText.ProjectRef(invoice));
            case ProtonInvoiceStep.Void:
                if (string.IsNullOrWhiteSpace(input.Reason))
                {
                    throw new ProtonToolInputException("Voiding needs a reason: ask the user why.");
                }

                InvoiceRules.CheckLive(isIssued, invoice.Status == InvoiceStatus.Void).ThrowIfDenied();
                if (invoice.Paid is > 0)
                {
                    throw new ProtonToolInputException("Payments are recorded against it: they are voided first.");
                }

                preview = new ProtonPreview($"Void invoice {invoice.Number}",
                    [new ProtonChange("Status", invoice.Status.ToString(), "Void"), new ProtonChange("Total", total, "no longer billed"), new ProtonChange("Reason", null, input.Reason.Trim())],
                    "Its number stays used. Reissue it as a new invoice if needed.",
                    Confirm: ProtonConfirm.Typed, Phrase: invoice.Number, Irreversible: true);
                return (new InvoiceStepArgs(invoice.Id, "void", null, input.Reason.Trim(), null), preview, MoneyText.ProjectRef(invoice));
            case ProtonInvoiceStep.Discard:
                InvoiceRules.CheckDraft(isIssued, invoice.Status == InvoiceStatus.Void).ThrowIfDenied();
                preview = new ProtonPreview($"Discard {name}", [new ProtonChange("Draft", total, "deleted")], null,
                    Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(invoice.Total), Irreversible: true);
                return (new InvoiceStepArgs(invoice.Id, "discard", null, null, null, draft), preview, MoneyText.ProjectRef(invoice));
            default:
                if (invoice.SendOn is null)
                {
                    throw new ProtonToolInputException("That invoice isn't scheduled to go out.");
                }

                preview = new ProtonPreview($"Stop {name} going out on {ProjectText.Date(invoice.SendOn)}",
                    [new ProtonChange("Goes out", ProjectText.Date(invoice.SendOn), "not scheduled")], "It stays a draft.");
                return (new InvoiceStepArgs(invoice.Id, "unschedule", null, null, null), preview, MoneyText.ProjectRef(invoice));
        }
    }

    protected override object? RequestOf(InvoiceStepArgs args) => args.Step switch
    {
        "issue" => new IssueInvoiceRequest { IssuedOn = args.IssuedOn },
        "void" => new VoidRequest { Reason = args.Reason! },
        _ => null,
    };

    protected override async Task<ProtonActionDone> ExecuteAsync(InvoiceStepArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        await context.RequireAsync(Steps[Enum.Parse<ProtonInvoiceStep>(args.Step, ignoreCase: true)]);
        if (args.Step is "issue" or "discard")
        {
            // the user typed the total the card showed: the draft must still be that draft
            var current = await invoices.GetAsync(args.InvoiceId, cancellationToken);
            if (args.Draft is null || !MoneyText.Same(MoneyText.RequestOf(current), args.Draft))
            {
                throw new BusinessRuleException(ErrorCodes.ProtonActionStale, "The draft changed since Proton proposed this. Ask again.");
            }
        }

        switch (args.Step)
        {
            case "issue":
                var issued = await invoices.IssueAsync(args.InvoiceId, new IssueInvoiceRequest { IssuedOn = args.IssuedOn }, cancellationToken);
                return new ProtonActionDone($"Issued invoice {issued.Number}.", "invoice", issued.Id, [MoneyText.ProjectRef(issued)]);
            case "void":
                var voided = await invoices.VoidAsync(args.InvoiceId, new VoidRequest { Reason = args.Reason! }, cancellationToken);
                return new ProtonActionDone($"Voided invoice {voided.Number}.", "invoice", voided.Id, [MoneyText.ProjectRef(voided)]);
            case "discard":
                await invoices.DiscardAsync(args.InvoiceId, cancellationToken);
                return new ProtonActionDone("Discarded the draft.", "invoice", args.InvoiceId, []);
            default:
                var unscheduled = await invoices.UnscheduleAsync(args.InvoiceId, cancellationToken);
                return new ProtonActionDone("It won't go out on its own now.", "invoice", unscheduled.Id, [MoneyText.ProjectRef(unscheduled)]);
        }
    }
}

public sealed record InvoiceDeliveryInput : ProtonActionInput
{
    [Description("An issued invoice (from list_invoices).")]
    public required long InvoiceId { get; init; }

    [Description("How it reached the client outside the app: whatsapp, or download (handed over). Emails are sent from the app.")]
    public required DeliveryChannel Channel { get; init; }

    [Description("Who it went to, as the user said.")]
    public string? Recipient { get; init; }

    public string? Note { get; init; }
}

/// <summary>
/// Records that an issued invoice reached the client outside the app (WhatsApp, by hand), through POST
/// /api/invoices/{id}/deliveries once confirmed. Nothing is sent: emailing it is done in the app.
/// </summary>
public sealed class ProposeInvoiceDeliveryTool(IInvoiceService invoices) : ProtonActionTool<InvoiceDeliveryInput, InvoiceStepArgs>
{
    public override string Name => "propose_record_invoice_delivery";

    public override string Description =>
        "Propose noting that an issued invoice was given to the client outside the app (WhatsApp, by hand). It sends " +
        "nothing. Nothing changes until the user confirms the card.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<InvoicesController>(nameof(InvoicesController.RecordDelivery));

    protected override async Task<(InvoiceStepArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        InvoiceDeliveryInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Channel == DeliveryChannel.Email)
        {
            throw new ProtonToolInputException("Emails are sent from the app itself, which records them: open the invoice for the user instead.");
        }

        var invoice = await invoices.GetAsync(input.InvoiceId, cancellationToken);
        InvoiceRules.CheckLive(invoice.Status is not InvoiceStatus.Draft and not InvoiceStatus.Void, invoice.Status == InvoiceStatus.Void).ThrowIfDenied();
        var delivery = new RecordDeliveryRequest { Channel = input.Channel, Recipient = MoneyText.Clean(input.Recipient), Note = MoneyText.Clean(input.Note) };
        return (new InvoiceStepArgs(invoice.Id, "delivery", null, null, delivery),
            new ProtonPreview($"Note that invoice {invoice.Number} was delivered",
                [
                    new ProtonChange("How", null, input.Channel == DeliveryChannel.Whatsapp ? "WhatsApp" : "Handed over"),
                    new ProtonChange("To", null, delivery.Recipient ?? invoice.Client.DisplayName),
                ]),
            MoneyText.ProjectRef(invoice));
    }

    protected override object? RequestOf(InvoiceStepArgs args) => args.Delivery;

    protected override async Task<ProtonActionDone> ExecuteAsync(InvoiceStepArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var invoice = await invoices.RecordDeliveryAsync(args.InvoiceId, args.Delivery!, cancellationToken);
        return new ProtonActionDone($"Noted that invoice {invoice.Number} was delivered.", "invoice", invoice.Id, [MoneyText.ProjectRef(invoice)]);
    }
}

public sealed record RecordPaymentArgs(long ProjectId, RecordPaymentRequest Request);

public sealed record RecordPaymentInput : ProtonActionInput
{
    public required long ProjectId { get; init; }

    [Description("The amount received, exactly as the user said.")]
    public required decimal Amount { get; init; }

    [Description("USD or LBP. Default USD.")]
    public string? Currency { get; init; }

    [Description("Today or earlier. Default today.")]
    public DateOnly? ReceivedOn { get; init; }

    public PaymentMethod? Method { get; init; }

    [Description("A cheque or transfer reference, as the user gave it.")]
    public string? Reference { get; init; }

    public string? Note { get; init; }

    [Description("The issued invoice it pays, from list_invoices. Leave out for a payment on account.")]
    public long? InvoiceId { get; init; }
}

/// <summary>
/// Records money received, through POST /api/projects/{id}/payments once confirmed, at the dated exchange rate in force
/// (Proton never sets a rate). The user types the amount to confirm.
/// </summary>
public sealed class ProposeRecordPaymentTool(IProjectService projects, IInvoiceService invoices, IPaymentService payments)
    : ProtonActionTool<RecordPaymentInput, RecordPaymentArgs>
{
    public override string Name => "propose_record_payment";

    public override string Description =>
        "Propose recording a payment the client made, against an invoice or on account, at the day's exchange rate. " +
        "The user confirms by typing the amount. The card warns when the same amount was already recorded that day.";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<PaymentsController>(nameof(PaymentsController.Record));

    protected override async Task<(RecordPaymentArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        RecordPaymentInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.Amount <= 0)
        {
            throw new ProtonToolInputException("The amount must be more than zero.");
        }

        var project = await projects.GetAsync(input.ProjectId, cancellationToken);
        var currency = (input.Currency ?? "USD").Trim().ToUpperInvariant();
        var request = new RecordPaymentRequest
        {
            Amount = input.Amount, Currency = currency, ReceivedOn = input.ReceivedOn, Method = input.Method ?? PaymentMethod.Bank,
            Reference = MoneyText.Clean(input.Reference), Note = MoneyText.Clean(input.Note), InvoiceId = input.InvoiceId,
        };
        var changes = new List<ProtonChange>
        {
            new("Project", null, project.Title),
            new("Amount", null, MoneyText.Amount(request.Amount, currency)),
            new("Received", null, ProjectText.Date(request.ReceivedOn ?? context.Today)),
            new("Method", null, request.Method.ToString()),
        };
        if (request.Reference is not null)
        {
            changes.Add(new ProtonChange("Reference", null, request.Reference));
        }

        string note;
        if (input.InvoiceId is { } invoiceId)
        {
            var invoice = await invoices.GetAsync(invoiceId, cancellationToken);
            if (invoice.Project.Id != project.Id)
            {
                throw new ProtonToolInputException("That invoice belongs to another project.");
            }

            InvoiceRules.CheckLive(invoice.Status is not InvoiceStatus.Draft and not InvoiceStatus.Void, invoice.Status == InvoiceStatus.Void).ThrowIfDenied();
            changes.Add(new ProtonChange("Against invoice", null, invoice.Number!));
            note = invoice.Balance is { } balance && invoice.Currency == currency
                ? $"Its balance goes from {MoneyText.Amount(balance, currency)} to {MoneyText.Amount(Math.Max(0, balance - request.Amount), currency)}."
                : "Applied to that invoice.";
        }
        else
        {
            note = "On account: it can be applied to an invoice later.";
        }

        return (new RecordPaymentArgs(project.Id, request),
            new ProtonPreview($"Record a payment from {project.Client?.DisplayName ?? project.Title}", changes,
                $"{await AlreadyRecordedAsync(project.Id, request, context, cancellationToken)}{note} Converted at the dated exchange rate in force on the day received.",
                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(request.Amount)),
            ProjectText.Ref(project));
    }

    private static readonly ProtonEndpoint PaymentList = ProtonEndpoint.Of<PaymentsController>(nameof(PaymentsController.List));

    /// <summary>
    /// The same amount already received on the project that day: often the same payment asked for twice. It isn't
    /// refused (two equal payments happen), but the card says so. Skipped for a user who can't list payments.
    /// </summary>
    private async Task<string> AlreadyRecordedAsync(long projectId, RecordPaymentRequest request, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (!await context.CanCallAsync(PaymentList))
        {
            return "";
        }

        var day = request.ReceivedOn ?? context.Today;
        var same = (await payments.ListAsync(new PaymentListQuery { ProjectId = projectId, ReceivedFrom = day, ReceivedTo = day }, cancellationToken)).Items
            .Count(p => p.Amount == request.Amount && p.Currency == request.Currency);
        return same == 0 ? "" : $"Careful: {same} payment(s) of {MoneyText.Amount(request.Amount, request.Currency)} received that day are already recorded. Confirm only if this is another one. ";
    }

    protected override object RequestOf(RecordPaymentArgs args) => args.Request;

    protected override async Task<ProtonActionDone> ExecuteAsync(RecordPaymentArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        var payment = await payments.RecordAsync(args.ProjectId, args.Request, cancellationToken);
        return new ProtonActionDone($"Recorded {MoneyText.Amount(payment.Amount, payment.Currency)} received.", "payment", payment.Id,
            [new ProtonRef(ProtonRefType.Project, payment.Project.Id, payment.Project.Title, payment.Domain)]);
    }
}

public sealed record PaymentStepArgs(long PaymentId, long? InvoiceId, string? Reason);

public sealed record PaymentStepInput : ProtonActionInput
{
    [Description("From list_payments.")]
    public required long PaymentId { get; init; }

    [Description("To apply a payment on account to an issued invoice of the same project.")]
    public long? ApplyToInvoiceId { get; init; }

    [Description("To void the payment instead: why, in the user's words.")]
    public string? VoidReason { get; init; }
}

/// <summary>
/// Applies a payment on account to an invoice, or voids a payment, through PUT /api/payments/{id}/invoice or POST
/// /api/payments/{id}/void once confirmed. Voiding takes the money back off the books: the user types the amount.
/// </summary>
public sealed class ProposePaymentStepTool(IPaymentService payments, IInvoiceService invoices) : ProtonActionTool<PaymentStepInput, PaymentStepArgs>
{
    public override string Name => "propose_payment_step";

    public override string Description =>
        "Propose applying a payment on account to an issued invoice, or voiding a payment recorded by mistake (the user " +
        "types the amount to confirm a void).";

    public override ProtonActionRisk Risk => ProtonActionRisk.Medium;

    public override ProtonEndpoint Endpoint { get; } = ProtonEndpoint.OfWrite<PaymentsController>(nameof(PaymentsController.Apply));

    private static readonly ProtonEndpoint VoidEndpoint = ProtonEndpoint.OfWrite<PaymentsController>(nameof(PaymentsController.Void));

    public override IReadOnlyList<ProtonEndpoint> StepEndpoints => [Endpoint, VoidEndpoint];

    protected override ProtonEndpoint StepOf(PaymentStepArgs args) => args.InvoiceId is null ? VoidEndpoint : Endpoint;

    protected override async Task<(PaymentStepArgs, ProtonPreview, ProtonRef?)> ProposeAsync(
        PaymentStepInput input, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (input.ApplyToInvoiceId is null == string.IsNullOrWhiteSpace(input.VoidReason))
        {
            throw new ProtonToolInputException("Give apply_to_invoice_id to apply it, or void_reason to void it: one of the two.");
        }

        var payment = await payments.GetAsync(input.PaymentId, cancellationToken);
        var amount = MoneyText.Amount(payment.Amount, payment.Currency);
        var project = new ProtonRef(ProtonRefType.Project, payment.Project.Id, payment.Project.Title, payment.Domain);
        if (input.ApplyToInvoiceId is { } invoiceId)
        {
            var invoice = await invoices.GetAsync(invoiceId, cancellationToken);
            if (invoice.Project.Id != payment.Project.Id)
            {
                throw new ProtonToolInputException("That invoice belongs to another project.");
            }

            return (new PaymentStepArgs(payment.Id, invoice.Id, null),
                new ProtonPreview($"Apply {amount} to invoice {invoice.Number}",
                    [new ProtonChange("Payment", payment.InvoiceNumber is null ? "on account" : $"on {payment.InvoiceNumber}", $"on {invoice.Number}")]),
                project);
        }

        await context.RequireAsync(VoidEndpoint);
        return (new PaymentStepArgs(payment.Id, null, input.VoidReason!.Trim()),
            new ProtonPreview($"Void the payment of {amount}",
                [new ProtonChange("Payment", amount, "void"), new ProtonChange("Reason", null, input.VoidReason.Trim())],
                payment.InvoiceNumber is null ? null : $"Invoice {payment.InvoiceNumber} owes it again.",
                Confirm: ProtonConfirm.Typed, Phrase: MoneyText.Phrase(payment.Amount), Irreversible: true),
            project);
    }

    protected override object? RequestOf(PaymentStepArgs args)
        => args.InvoiceId is { } invoiceId ? new ApplyPaymentRequest { InvoiceId = invoiceId } : new VoidRequest { Reason = args.Reason! };

    protected override async Task<ProtonActionDone> ExecuteAsync(PaymentStepArgs args, ProtonToolContext context, CancellationToken cancellationToken)
    {
        if (args.InvoiceId is { } invoiceId)
        {
            var applied = await payments.ApplyAsync(args.PaymentId, new ApplyPaymentRequest { InvoiceId = invoiceId }, cancellationToken);
            return new ProtonActionDone($"Applied it to invoice {applied.InvoiceNumber}.", "payment", applied.Id, []);
        }

        await context.RequireAsync(VoidEndpoint);
        var voided = await payments.VoidAsync(args.PaymentId, new VoidRequest { Reason = args.Reason! }, cancellationToken);
        return new ProtonActionDone("Voided the payment.", "payment", voided.Id, []);
    }
}

/// <summary>Shared money wording: amounts as the app writes them, and what the user types to confirm money.</summary>
public static class MoneyText
{
    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static string Amount(decimal amount, string? currency)
        => $"{(currency is null ? "" : currency + " ")}{amount.ToString("#,0.##", CultureInfo.InvariantCulture)}";

    /// <summary>The amount as the user types it to confirm: digits and a decimal point, no separators ("4500", "4500.5").</summary>
    public static string Phrase(decimal amount) => amount.ToString("0.##", CultureInfo.InvariantCulture);

    public static IReadOnlyList<InvoiceLineRequest> LinesOf(IReadOnlyList<InvoiceLineInput> lines)
    {
        if (lines.Count == 0)
        {
            throw new ProtonToolInputException("An invoice needs at least one line.");
        }

        return lines.Select(l => new InvoiceLineRequest { Description = l.Description.Trim(), Amount = l.Amount }).ToList();
    }

    public static SaveInvoiceRequest RequestOf(InvoiceResponse i) => new()
    {
        Lines = i.Lines.OrderBy(l => l.Position).Select(l => new InvoiceLineRequest { Description = l.Description, Amount = l.Amount }).ToList(),
        TermsDays = i.TermsDays, Notes = i.Notes, BillToName = i.BillToName, BillToContact = i.BillToContact,
    };

    public static bool SameLines(IReadOnlyList<InvoiceLineRequest> a, IReadOnlyList<InvoiceLineRequest> b) => a.SequenceEqual(b);

    public static bool Same(SaveInvoiceRequest a, SaveInvoiceRequest b)
        => SameLines(a.Lines, b.Lines) && a.TermsDays == b.TermsDays && a.Notes == b.Notes && a.BillToName == b.BillToName && a.BillToContact == b.BillToContact;

    public static string Lines(IReadOnlyList<InvoiceLineRequest> lines, string? currency)
        => string.Join("; ", lines.Select(l => $"{l.Description}: {Amount(l.Amount, currency)}"));

    public static ProtonRef ProjectRef(InvoiceSummaryResponse i) => new(ProtonRefType.Project, i.Project.Id, i.Project.Title, i.Domain);
}
