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
using ID_Indetails.api.Repositories.Billing.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;

namespace ID_Indetails.api.Services.Billing.Implementation;

/// <summary>Payments of projects in the caller's visible domains; any other payment reads as not found.</summary>
public sealed class PaymentService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IInvoiceRepository invoices,
    IPaymentRepository payments,
    IExchangeRateRepository exchangeRates,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IPaymentService
{
    private const string NotFoundMessage = "Payment not found.";

    public async Task<PagedResponse<PaymentResponse>> ListAsync(PaymentListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialView);
        var (items, total) = await payments.SearchAsync(
            new PaymentSearch(
                actor.VisibleDomains, query.ProjectId, query.ClientId, query.InvoiceId, query.IncludeVoided == true,
                query.Domain, query.Page, query.PageSize),
            cancellationToken);

        return new PagedResponse<PaymentResponse>(items.Select(ToResponse).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<PaymentResponse> GetAsync(long paymentId, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialView);
        return await DescribeAsync(actor, paymentId, cancellationToken);
    }

    public async Task<PaymentResponse> RecordAsync(long projectId, RecordPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await projects.GetForUpdateAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        var payment = await RecordForProjectAsync(project, request, actor.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, payment.Id, cancellationToken);
    }

    public async Task<Payment> RecordForProjectAsync(
        Project project,
        RecordPaymentRequest request,
        long actorId,
        CancellationToken cancellationToken = default)
    {
        Throw(InvoiceRules.CheckBillable(project.BillingType, project.ClientId));

        var today = clock.CompanyToday;
        var receivedOn = request.ReceivedOn ?? today;
        Throw(PaymentRules.CheckReceivedOn(receivedOn, today));

        var currency = request.Currency.ToUpperInvariant();
        var rate = await RateAsync(currency, request.FxRate, receivedOn, cancellationToken);
        var amountUsd = PaymentRules.ToUsd(request.Amount, rate);
        Throw(PaymentRules.CheckUsd(amountUsd));
        Throw(PaymentRules.CheckInitial(request.IsInitial, project.DomainCode, await payments.HasLiveInitialAsync(project.Id, cancellationToken)));

        Invoice? invoice = null;
        if (request.InvoiceId is { } invoiceId)
        {
            invoice = await CheckInvoiceAsync(project.Id, invoiceId, amountUsd, cancellationToken);
        }

        var payment = new Payment
        {
            ProjectId = project.Id,
            ClientId = project.ClientId!.Value,
            InvoiceId = invoice?.Id,
            IsInitial = request.IsInitial,
            Amount = request.Amount,
            Currency = currency,
            FxRate = rate,
            AmountUsd = amountUsd,
            ReceivedOn = receivedOn,
            Method = request.Method,
            Reference = Clean(request.Reference),
            Note = Clean(request.Note),
            RecordedAt = clock.UtcNow,
            RecordedById = actorId,
        };
        payments.Add(payment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity.Add(PaymentEvent(project.DomainCode, payment, ActivityEventKinds.PaymentRecorded, actorId,
            new { invoiceNumber = invoice?.Number, currency, method = request.Method, isInitial = request.IsInitial }));
        // Whoever sees the domain's money; the payload never holds amounts.
        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.PaymentRecorded, actorId, project.DomainCode, project.Id, "payment", payment.Id,
                new { invoiceNumber = invoice?.Number, isInitial = request.IsInitial }),
            await notifications.HoldersAsync(project.DomainCode, PermissionCodes.FinancialView, cancellationToken),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return payment;
    }

    public async Task<PaymentResponse> ApplyAsync(long paymentId, ApplyPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var (payment, domain) = await VisiblePaymentForUpdateAsync(actor, paymentId, cancellationToken);
        Throw(PaymentRules.CheckApply(payment.VoidedAt is not null, payment.InvoiceId));

        var invoice = await CheckInvoiceAsync(payment.ProjectId, request.InvoiceId, payment.AmountUsd, cancellationToken);
        payment.InvoiceId = invoice.Id;
        activity.Add(PaymentEvent(domain, payment, ActivityEventKinds.PaymentApplied, actor.UserId, new { invoiceNumber = invoice.Number }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, payment.Id, cancellationToken);
    }

    public async Task<PaymentResponse> VoidAsync(long paymentId, VoidRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var (payment, domain) = await VisiblePaymentForUpdateAsync(actor, paymentId, cancellationToken);
        Throw(PaymentRules.CheckVoid(payment.VoidedAt is not null));

        payment.VoidedAt = clock.UtcNow;
        payment.VoidReason = request.Reason.Trim();
        payment.VoidedById = actor.UserId;
        activity.Add(PaymentEvent(domain, payment, ActivityEventKinds.PaymentVoided, actor.UserId, new { reason = payment.VoidReason }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, payment.Id, cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    /// <summary>The typed rate, 1 for US dollars, or else the dated rate in force on the day received.</summary>
    private async Task<decimal> RateAsync(string currency, decimal? typed, DateOnly receivedOn, CancellationToken cancellationToken)
    {
        if (currency == PaymentRules.BaseCurrency)
        {
            return 1m;
        }

        if (typed is { } rate)
        {
            return rate;
        }

        var inForce = await exchangeRates.GetInForceAsync(currency, receivedOn, cancellationToken)
            ?? throw new BusinessRuleException(
                ErrorCodes.ExchangeRateMissing, $"There is no {currency} exchange rate for that day. Enter the rate used.");
        return inForce.RatePerUsd;
    }

    /// <summary>The invoice, locked so its balance can't change underneath, once the payment fits it.</summary>
    private async Task<Invoice> CheckInvoiceAsync(long projectId, long invoiceId, decimal amountUsd, CancellationToken cancellationToken)
    {
        // Only this project's invoices are looked up (and locked), so another project's or domain's
        // invoice ids can't be probed.
        var invoice = await invoices.GetForProjectForUpdateAsync(projectId, invoiceId, cancellationToken)
            ?? throw new RequestValidationException(ErrorCodes.ValidationFailed, "invoiceId", "This project has no such invoice.");

        // the payment is in USD, so the invoice's balance is too: its total at the rate in force on its issue day
        var totalUsd = invoice.Currency == PaymentRules.BaseCurrency || invoice.IssuedAt is not { } issuedAt
            ? invoice.Total
            : PaymentRules.ToUsd(invoice.Total, await RateAsync(invoice.Currency, null, clock.ToCompanyDate(issuedAt), cancellationToken));
        var balance = InvoiceRules.Balance(totalUsd, await invoices.PaidForUpdateAsync(invoice.Id, cancellationToken));
        Throw(PaymentRules.CheckInvoice(
            projectId, invoice.ProjectId, invoice.IssuedAt is not null, invoice.VoidedAt is not null, balance, amountUsd));
        return invoice;
    }

    private async Task<(Payment Payment, string Domain)> VisiblePaymentForUpdateAsync(
        UserAccess actor,
        long paymentId,
        CancellationToken cancellationToken)
    {
        var payment = await payments.GetForUpdateAsync(paymentId, cancellationToken)
            ?? throw new NotFoundException(NotFoundMessage);
        var project = await projects.GetAsync(payment.ProjectId, cancellationToken);
        return project is not null && actor.CanSee(project.DomainCode)
            ? (payment, project.DomainCode)
            : throw new NotFoundException(NotFoundMessage);
    }

    private async Task<PaymentResponse> DescribeAsync(UserAccess actor, long paymentId, CancellationToken cancellationToken)
    {
        var payment = await payments.GetAsync(paymentId, cancellationToken);
        return payment is not null && actor.CanSee(payment.Project.DomainCode)
            ? ToResponse(payment)
            : throw new NotFoundException(NotFoundMessage);
    }

    private ActivityEvent PaymentEvent(string domain, Payment payment, string kind, long actorId, object payload) => new()
    {
        DomainCode = domain,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = payment.ProjectId,
        SubjectType = "payment",
        SubjectId = payment.Id,
        Amount = payment.AmountUsd,
        Payload = JsonSerializer.Serialize(payload, EventJson),
    };

    private static readonly JsonSerializerOptions EventJson = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static PaymentResponse ToResponse(Payment p) => new()
    {
        Id = p.Id,
        Domain = p.Project.DomainCode,
        Project = new InvoiceProjectResponse(p.Project.Id, p.Project.Title, p.Project.Location),
        Client = new InvoiceClientResponse(p.Client.Id, ClientRules.DisplayName(p.Client.Name, p.Client.Organisation)),
        InvoiceId = p.InvoiceId,
        InvoiceNumber = p.Invoice?.Number,
        IsInitial = p.IsInitial,
        Amount = p.Amount,
        Currency = p.Currency,
        FxRate = p.FxRate,
        AmountUsd = p.AmountUsd,
        ReceivedOn = p.ReceivedOn,
        Method = p.Method,
        Reference = p.Reference,
        Note = p.Note,
        RecordedAt = p.RecordedAt,
        VoidedAt = p.VoidedAt,
        VoidReason = p.VoidReason,
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
