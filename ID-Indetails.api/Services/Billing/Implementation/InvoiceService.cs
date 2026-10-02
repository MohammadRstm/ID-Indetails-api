using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;

namespace ID_Indetails.api.Services.Billing.Implementation;

/// <summary>
/// Invoices of projects in the caller's visible domains; any other invoice reads as not found.
/// Calendar days (issue and due dates) are stored at noon UTC, which is the same day in the
/// company time zone; an invoice issued today keeps the real time of issue.
/// </summary>
public sealed class InvoiceService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IInvoiceRepository invoices,
    IPaymentRepository payments,
    IBillingSettingsRepository billingSettings,
    IUserRepository users,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IInvoiceService
{
    private const int FallbackTermsDays = 30;
    private const string NotFoundMessage = "Invoice not found.";

    /// <summary>How the scheduled sending reads an invoice: in any domain, with what was paid.</summary>
    private static readonly UserAccess Sending = new(0, RoleCodes.SuperAdmin, null, [PermissionCodes.FinancialView], false, DomainCodes.All);

    public async Task<PagedResponse<InvoiceSummaryResponse>> ListAsync(InvoiceListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var (rows, total) = await invoices.SearchAsync(
            new InvoiceSearch(
                actor.VisibleDomains, query.ProjectId, query.ClientId, query.Status, query.Overdue, query.Domain,
                clock.CompanyToday, query.Page, query.PageSize),
            cancellationToken);

        return new PagedResponse<InvoiceSummaryResponse>(
            rows.Select(r => Summary(r, actor.Has(PermissionCodes.FinancialView))).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<InvoiceResponse> GetAsync(long invoiceId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        return await DescribeAsync(actor, invoiceId, cancellationToken);
    }

    public async Task<InvoiceResponse> CreateAsync(long projectId, SaveInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        Throw(InvoiceRules.CheckBillable(project.BillingType, project.ClientId));
        var settings = await billingSettings.GetAsync(project.DomainCode, cancellationToken);

        var invoice = new Invoice
        {
            ProjectId = project.Id,
            ClientId = project.ClientId!.Value,
            Currency = project.RevenueCurrency,
            TermsDays = request.TermsDays ?? settings?.DefaultTermsDays ?? FallbackTermsDays,
            CreatedAt = clock.UtcNow,
            CreatedById = actor.UserId,
        };
        Apply(invoice, request);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        invoices.Add(invoice);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        activity.Add(InvoiceEvent(project.DomainCode, invoice, ActivityEventKinds.InvoiceCreated, actor.UserId, invoice.Total));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, invoice.Id, cancellationToken);
    }

    public async Task<InvoiceResponse> UpdateAsync(long invoiceId, SaveInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var (invoice, domain) = await VisibleInvoiceForUpdateAsync(actor, invoiceId, cancellationToken);
        Throw(InvoiceRules.CheckDraft(invoice.IssuedAt is not null, invoice.VoidedAt is not null));

        if (request.TermsDays is { } terms)
        {
            invoice.TermsDays = terms;
        }

        Apply(invoice, request);
        invoice.UpdatedAt = clock.UtcNow;
        activity.Add(InvoiceEvent(domain, invoice, ActivityEventKinds.InvoiceUpdated, actor.UserId, invoice.Total));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, invoice.Id, cancellationToken);
    }

    public async Task DiscardAsync(long invoiceId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var (invoice, domain) = await VisibleInvoiceForUpdateAsync(actor, invoiceId, cancellationToken);
        Throw(InvoiceRules.CheckDraft(invoice.IssuedAt is not null, invoice.VoidedAt is not null));

        activity.Add(InvoiceEvent(domain, invoice, ActivityEventKinds.InvoiceDiscarded, actor.UserId, invoice.Total));
        invoices.Remove(invoice);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<InvoiceResponse> IssueAsync(long invoiceId, IssueInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var (invoice, domain) = await VisibleInvoiceForUpdateAsync(actor, invoiceId, cancellationToken);
        Throw(InvoiceRules.CheckDraft(invoice.IssuedAt is not null, invoice.VoidedAt is not null));

        var today = clock.CompanyToday;
        var issuedOn = request.IssuedOn ?? today;
        Throw(InvoiceRules.CheckIssueDate(issuedOn, today));

        await IssueAsync(invoice, domain, issuedOn, actor.UserId, automatic: false, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, invoice.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceResponse>> CreatePlanAsync(
        long projectId,
        InvoicePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        Throw(InvoiceRules.CheckBillable(project.BillingType, project.ClientId));
        // they are issued with nobody there, so the domain must already be able to issue invoices
        var settings = await SettingsToIssueAsync(project.DomainCode, cancellationToken);
        foreach (var planned in request.Invoices)
        {
            Throw(InvoiceRules.CheckSendOn(planned.SendOn, clock.CompanyToday));
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var created = new List<Invoice>();
        foreach (var planned in request.Invoices)
        {
            var invoice = new Invoice
            {
                ProjectId = project.Id,
                ClientId = project.ClientId!.Value,
                Currency = project.RevenueCurrency,
                TermsDays = request.TermsDays ?? settings.DefaultTermsDays,
                CreatedAt = clock.UtcNow,
                CreatedById = actor.UserId,
                SendOn = planned.SendOn,
                SendTo = Clean(request.SendTo),
                ScheduledById = actor.UserId,
            };
            Apply(invoice, new SaveInvoiceRequest
            {
                Lines = [new InvoiceLineRequest { Description = planned.Description, Amount = planned.Amount }],
                Notes = request.Notes,
            });
            invoices.Add(invoice);
            created.Add(invoice);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var invoice in created)
        {
            activity.Add(InvoiceEvent(project.DomainCode, invoice, ActivityEventKinds.InvoiceCreated, actor.UserId, invoice.Total,
                new { sendOn = invoice.SendOn }));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var plan = new List<InvoiceResponse>();
        foreach (var invoice in created)
        {
            plan.Add(await DescribeAsync(actor, invoice.Id, cancellationToken));
        }

        return plan;
    }

    public async Task<InvoiceResponse> ScheduleAsync(long invoiceId, ScheduleInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var (invoice, domain) = await VisibleInvoiceForUpdateAsync(actor, invoiceId, cancellationToken);
        Throw(InvoiceRules.CheckDraft(invoice.IssuedAt is not null, invoice.VoidedAt is not null));
        Throw(InvoiceRules.CheckSendOn(request.SendOn, clock.CompanyToday));
        await SettingsToIssueAsync(domain, cancellationToken);

        invoice.SendOn = request.SendOn;
        invoice.SendTo = Clean(request.SendTo);
        invoice.ScheduledById = actor.UserId;
        invoice.UpdatedAt = clock.UtcNow;
        activity.Add(InvoiceEvent(domain, invoice, ActivityEventKinds.InvoiceScheduled, actor.UserId, invoice.Total, new { sendOn = invoice.SendOn }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, invoice.Id, cancellationToken);
    }

    /// <summary>It stays a draft, sent by hand. Nothing to do when it isn't scheduled.</summary>
    public async Task<InvoiceResponse> UnscheduleAsync(long invoiceId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var (invoice, domain) = await VisibleInvoiceForUpdateAsync(actor, invoiceId, cancellationToken);
        Throw(InvoiceRules.CheckDraft(invoice.IssuedAt is not null, invoice.VoidedAt is not null));

        if (invoice.SendOn is not null)
        {
            EndSchedule(invoice);
            invoice.UpdatedAt = clock.UtcNow;
            activity.Add(InvoiceEvent(domain, invoice, ActivityEventKinds.InvoiceScheduled, actor.UserId, invoice.Total));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(actor, invoice.Id, cancellationToken);
    }

    public async Task<bool> SendScheduledAsync(
        long invoiceId,
        Func<InvoiceResponse, string, CancellationToken, Task> email,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var invoice = await invoices.GetForUpdateAsync(invoiceId, cancellationToken);
        // sent by hand, unscheduled, discarded or moved on meanwhile
        if (invoice is not { IssuedAt: null, VoidedAt: null, SendOn: { } sendOn } || sendOn > clock.CompanyToday)
        {
            return false;
        }

        var domain = (await projects.GetAsync(invoice.ProjectId, cancellationToken))!.DomainCode;
        var scheduledBy = invoice.ScheduledById;
        if (invoice.SendTo is not { } to)
        {
            // nowhere to email it, so whoever scheduled it sends it (on WhatsApp, say), which issues it
            await notifications.SendOnceAsync(
                new NotificationDraft(NotificationKinds.InvoiceSendDue, null, domain, invoice.ProjectId, "invoice", invoice.Id, new { sendOn }),
                $"invoice-send-due:{invoice.Id}:{sendOn:yyyy-MM-dd}", ToWhoScheduled(scheduledBy), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await IssueAsync(invoice, domain, clock.CompanyToday, scheduledBy, automatic: true, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var issued = await DescribeAsync(Sending, invoice.Id, cancellationToken);
        // what was paid on account may have settled it: the client isn't sent a bill they have paid
        var emailed = issued.Balance > 0;
        if (emailed)
        {
            await email(issued, to, cancellationToken);
            invoices.AddDelivery(new InvoiceDelivery
            {
                InvoiceId = invoice.Id,
                Channel = DeliveryChannel.Email,
                Recipient = to,
                Note = "Sent automatically, as scheduled",
                SentAt = clock.UtcNow,
                SentById = scheduledBy,
            });
            activity.Add(InvoiceEvent(domain, invoice, ActivityEventKinds.InvoiceSent, scheduledBy, null,
                new { number = invoice.Number, channel = DeliveryChannel.Email, automatic = true }));
        }

        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.InvoiceSentAutomatically, null, domain, invoice.ProjectId, "invoice", invoice.Id,
                new { number = invoice.Number, to = emailed ? to : null }),
            ToWhoScheduled(scheduledBy),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task ScheduledSendFailedAsync(long invoiceId, CancellationToken cancellationToken = default)
    {
        if (await invoices.GetAsync(invoiceId, cancellationToken) is not { Invoice: { IssuedAt: null, SendOn: { } sendOn } invoice })
        {
            return;
        }

        await notifications.SendOnceAsync(
            new NotificationDraft(NotificationKinds.InvoiceSendFailed, null, invoice.Project.DomainCode, invoice.ProjectId, "invoice", invoice.Id,
                new { sendTo = invoice.SendTo }),
            $"invoice-send-failed:{invoice.Id}:{sendOn:yyyy-MM-dd}", ToWhoScheduled(invoice.ScheduledById), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Numbers the locked draft and dates it, pays it with what is on account, and tells whoever sees the domain's
    /// money. Its schedule ends: a draft sent before its day isn't sent again on it.
    /// </summary>
    private async Task IssueAsync(Invoice invoice, string domain, DateOnly issuedOn, long? actorId, bool automatic, CancellationToken cancellationToken)
    {
        var today = clock.CompanyToday;
        var settings = await SettingsToIssueAsync(domain, cancellationToken);
        var sequence = await invoices.NextSequenceAsync(domain, issuedOn.Year, cancellationToken);
        invoice.Number = InvoiceRules.Number(settings.InvoicePrefix, issuedOn.Year, sequence);
        invoice.IssuedAt = issuedOn == today ? clock.UtcNow : Noon(issuedOn);
        invoice.DueAt = Noon(issuedOn.AddDays(invoice.TermsDays));
        invoice.UpdatedAt = clock.UtcNow;
        EndSchedule(invoice);

        activity.Add(InvoiceEvent(domain, invoice, ActivityEventKinds.InvoiceIssued, actorId, invoice.Total,
            automatic ? new { number = invoice.Number, automatic = true } : (object)new { number = invoice.Number }));

        // What the client already paid on account pays the new invoice, so it isn't shown as owed (or chased) twice.
        var onAccount = await payments.OnAccountForUpdateAsync(invoice.ProjectId, cancellationToken);
        var applied = PaymentRules.ToApply(invoice.Total, onAccount.Select(p => (p.Id, p.AmountUsd))).ToHashSet();
        foreach (var payment in onAccount.Where(p => applied.Contains(p.Id)))
        {
            payment.InvoiceId = invoice.Id;
            activity.Add(new ActivityEvent
            {
                DomainCode = domain,
                Kind = ActivityEventKinds.PaymentApplied,
                OccurredAt = clock.UtcNow,
                ActorId = actorId,
                ProjectId = payment.ProjectId,
                SubjectType = "payment",
                SubjectId = payment.Id,
                Amount = payment.AmountUsd,
                Payload = JsonSerializer.Serialize(new { invoiceNumber = invoice.Number, automatic = true }, EventJson),
            });
        }
        // Whoever sees the domain's money; the payload never holds amounts.
        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.InvoiceIssued, actorId, domain, invoice.ProjectId, "invoice", invoice.Id,
                new { number = invoice.Number }),
            await notifications.HoldersAsync(domain, PermissionCodes.FinancialView, cancellationToken),
            cancellationToken);
    }

    private async Task<BillingSettings> SettingsToIssueAsync(string domain, CancellationToken cancellationToken)
        => await billingSettings.GetAsync(domain, cancellationToken)
            ?? throw new BusinessRuleException(
                ErrorCodes.BillingSettingsMissing, "Set up this domain's billing settings before issuing invoices.");

    private static void EndSchedule(Invoice invoice)
    {
        invoice.SendOn = null;
        invoice.SendTo = null;
        invoice.ScheduledById = null;
    }

    private static long[] ToWhoScheduled(long? scheduledById) => scheduledById is { } id ? [id] : [];

    public async Task<InvoiceResponse> RecordDeliveryAsync(
        long invoiceId,
        RecordDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var (invoice, domain) = await VisibleInvoiceForUpdateAsync(actor, invoiceId, cancellationToken);
        Throw(InvoiceRules.CheckLive(invoice.IssuedAt is not null, invoice.VoidedAt is not null));

        invoices.AddDelivery(new InvoiceDelivery
        {
            InvoiceId = invoice.Id,
            Channel = request.Channel,
            Recipient = Clean(request.Recipient),
            Note = Clean(request.Note),
            SentAt = clock.UtcNow,
            SentById = actor.UserId,
        });
        activity.Add(InvoiceEvent(domain, invoice, ActivityEventKinds.InvoiceSent, actor.UserId, null,
            new { number = invoice.Number, channel = request.Channel }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, invoice.Id, cancellationToken);
    }

    public async Task<InvoiceResponse> VoidAsync(long invoiceId, VoidRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var (invoice, domain) = await VisibleInvoiceForUpdateAsync(actor, invoiceId, cancellationToken);
        var hasPayments = await invoices.PaidForUpdateAsync(invoice.Id, cancellationToken) > 0;
        Throw(InvoiceRules.CheckVoid(invoice.IssuedAt is not null, invoice.VoidedAt is not null, hasPayments));

        invoice.VoidedAt = clock.UtcNow;
        invoice.VoidReason = request.Reason.Trim();
        invoice.VoidedById = actor.UserId;
        activity.Add(InvoiceEvent(domain, invoice, ActivityEventKinds.InvoiceVoided, actor.UserId, invoice.Total,
            new { number = invoice.Number, reason = invoice.VoidReason }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, invoice.Id, cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    /// <summary>The invoice, tracked and locked, with its project's domain; hidden invoices read as not found.</summary>
    private async Task<(Invoice Invoice, string Domain)> VisibleInvoiceForUpdateAsync(
        UserAccess actor,
        long invoiceId,
        CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetForUpdateAsync(invoiceId, cancellationToken)
            ?? throw new NotFoundException(NotFoundMessage);
        var project = await projects.GetAsync(invoice.ProjectId, cancellationToken);
        return project is not null && actor.CanSee(project.DomainCode)
            ? (invoice, project.DomainCode)
            : throw new NotFoundException(NotFoundMessage);
    }

    /// <summary>Replaces the lines in place (positions 1..n stay unique) and the free-text fields.</summary>
    private static void Apply(Invoice invoice, SaveInvoiceRequest request)
    {
        var lines = invoice.Lines.OrderBy(l => l.Position).ToList();
        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = i < lines.Count ? lines[i] : new InvoiceLine { Position = i + 1 };
            line.Description = request.Lines[i].Description.Trim();
            line.Amount = request.Lines[i].Amount;
            if (i >= lines.Count)
            {
                invoice.Lines.Add(line);
            }
        }

        foreach (var extra in lines.Skip(request.Lines.Count))
        {
            invoice.Lines.Remove(extra);
        }

        invoice.Total = request.Lines.Sum(l => l.Amount);
        invoice.Notes = Clean(request.Notes);
        invoice.BillToName = Clean(request.BillToName);
        invoice.BillToContact = Clean(request.BillToContact);
    }

    private async Task<InvoiceResponse> DescribeAsync(UserAccess actor, long invoiceId, CancellationToken cancellationToken)
    {
        var row = await invoices.GetAsync(invoiceId, cancellationToken);
        if (row is null || !actor.CanSee(row.Invoice.Project.DomainCode))
        {
            throw new NotFoundException(NotFoundMessage);
        }

        var i = row.Invoice;
        var creator = i.CreatedById is { } creatorId ? await users.GetByIdAsync(creatorId, cancellationToken) : null;
        var scheduler = i.ScheduledById is { } schedulerId ? await users.GetByIdAsync(schedulerId, cancellationToken) : null;
        return new InvoiceResponse(Summary(row, actor.Has(PermissionCodes.FinancialView)))
        {
            CreatedBy = creator is null ? null : new PersonRefResponse(creator.Id, PersonName.Of(creator.FirstName, creator.LastName, creator.Email)),
            SendTo = i.SendTo,
            ScheduledBy = scheduler is null ? null : new PersonRefResponse(scheduler.Id, PersonName.Of(scheduler.FirstName, scheduler.LastName, scheduler.Email)),
            TermsDays = i.TermsDays,
            Notes = i.Notes,
            BillToName = i.BillToName,
            BillToContact = i.BillToContact,
            VoidedAt = i.VoidedAt,
            VoidReason = i.VoidReason,
            Lines = i.Lines.Select(l => new InvoiceLineResponse(l.Id, l.Position, l.Description, l.Amount)).ToList(),
            Deliveries = i.Deliveries.Select(d => new InvoiceDeliveryResponse(d.Id, d.Channel, d.Recipient, d.SentAt, d.Note)).ToList(),
        };
    }

    /// <summary>
    /// Without financial:view, what has been paid stays hidden: no paid, balance or overdue figures, and a partly or
    /// fully paid invoice reads as sent. The caller still sees the amounts they invoice.
    /// </summary>
    private InvoiceSummaryResponse Summary(InvoiceRow row, bool seesMoney)
    {
        var i = row.Invoice;
        var status = InvoiceRules.Status(i.VoidedAt is not null, i.IssuedAt is not null, i.Total, row.Paid);
        var shownStatus = seesMoney || status is not (InvoiceStatus.Partial or InvoiceStatus.Paid) ? status : InvoiceStatus.Sent;
        var dueOn = i.DueAt is { } due ? DateOnly.FromDateTime(due) : (DateOnly?)null;
        return new InvoiceSummaryResponse
        {
            Id = i.Id,
            Domain = i.Project.DomainCode,
            Project = new InvoiceProjectResponse(i.Project.Id, i.Project.Title, i.Project.Location),
            Client = new InvoiceClientResponse(i.Client.Id, ClientRules.DisplayName(i.Client.Name, i.Client.Organisation)),
            Number = i.Number,
            Status = shownStatus,
            IsOverdue = seesMoney ? InvoiceRules.IsOverdue(status, dueOn, clock.CompanyToday) : null,
            Currency = i.Currency,
            Total = i.Total,
            Paid = seesMoney ? row.Paid : null,
            Balance = seesMoney ? InvoiceRules.Balance(i.Total, row.Paid) : null,
            IssuedAt = i.IssuedAt,
            DueOn = dueOn,
            CreatedAt = i.CreatedAt,
            SendOn = i.SendOn,
        };
    }

    private ActivityEvent InvoiceEvent(string domain, Invoice invoice, string kind, long? actorId, decimal? amount, object? payload = null) => new()
    {
        DomainCode = domain,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = invoice.ProjectId,
        SubjectType = "invoice",
        SubjectId = invoice.Id,
        Amount = amount,
        Payload = payload is null ? null : JsonSerializer.Serialize(payload, EventJson),
    };

    private static readonly JsonSerializerOptions EventJson = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static DateTime Noon(DateOnly day) => day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
