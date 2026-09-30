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
    IBillingSettingsRepository billingSettings,
    IUserRepository users,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IInvoiceService
{
    private const int FallbackTermsDays = 30;
    private const string NotFoundMessage = "Invoice not found.";

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

        var settings = await billingSettings.GetAsync(domain, cancellationToken)
            ?? throw new BusinessRuleException(
                ErrorCodes.BillingSettingsMissing, "Set up this domain's billing settings before issuing invoices.");

        var sequence = await invoices.NextSequenceAsync(domain, issuedOn.Year, cancellationToken);
        invoice.Number = InvoiceRules.Number(settings.InvoicePrefix, issuedOn.Year, sequence);
        invoice.IssuedAt = issuedOn == today ? clock.UtcNow : Noon(issuedOn);
        invoice.DueAt = Noon(issuedOn.AddDays(invoice.TermsDays));
        invoice.UpdatedAt = clock.UtcNow;

        activity.Add(InvoiceEvent(domain, invoice, ActivityEventKinds.InvoiceIssued, actor.UserId, invoice.Total, new { number = invoice.Number }));
        // Whoever sees the domain's money; the payload never holds amounts.
        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.InvoiceIssued, actor.UserId, domain, invoice.ProjectId, "invoice", invoice.Id,
                new { number = invoice.Number }),
            await notifications.HoldersAsync(domain, PermissionCodes.FinancialView, cancellationToken),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, invoice.Id, cancellationToken);
    }

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
        return new InvoiceResponse(Summary(row, actor.Has(PermissionCodes.FinancialView)))
        {
            CreatedBy = creator is null ? null : new PersonRefResponse(creator.Id, PersonName.Of(creator.FirstName, creator.LastName, creator.Email)),
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
            Project = new InvoiceProjectResponse(i.Project.Id, i.Project.Title),
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
        };
    }

    private ActivityEvent InvoiceEvent(string domain, Invoice invoice, string kind, long actorId, decimal? amount, object? payload = null) => new()
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
