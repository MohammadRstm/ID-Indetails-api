using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;

namespace ID_Indetails.api.Services.Billing.Abstraction;

public interface IInvoiceService
{
    Task<PagedResponse<InvoiceSummaryResponse>> ListAsync(InvoiceListQuery query, CancellationToken cancellationToken = default);

    Task<InvoiceResponse> GetAsync(long invoiceId, CancellationToken cancellationToken = default);

    /// <summary>A new draft for the project's client.</summary>
    Task<InvoiceResponse> CreateAsync(long projectId, SaveInvoiceRequest request, CancellationToken cancellationToken = default);

    Task<InvoiceResponse> UpdateAsync(long invoiceId, SaveInvoiceRequest request, CancellationToken cancellationToken = default);

    Task DiscardAsync(long invoiceId, CancellationToken cancellationToken = default);

    Task<InvoiceResponse> IssueAsync(long invoiceId, IssueInvoiceRequest request, CancellationToken cancellationToken = default);

    Task<InvoiceResponse> RecordDeliveryAsync(long invoiceId, RecordDeliveryRequest request, CancellationToken cancellationToken = default);

    Task<InvoiceResponse> VoidAsync(long invoiceId, VoidRequest request, CancellationToken cancellationToken = default);

    /// <summary>Auto invoice: the drafts, each scheduled for its day. Use <c>IInvoiceScheduleService</c>, which also sends today's.</summary>
    Task<IReadOnlyList<InvoiceResponse>> CreatePlanAsync(long projectId, InvoicePlanRequest request, CancellationToken cancellationToken = default);

    Task<InvoiceResponse> ScheduleAsync(long invoiceId, ScheduleInvoiceRequest request, CancellationToken cancellationToken = default);

    Task<InvoiceResponse> UnscheduleAsync(long invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// For the scheduled sending, with nobody signed in: issues a scheduled draft whose day has come, in the name of
    /// whoever scheduled it, and has <paramref name="email"/> send it before anything is saved, so a failed email
    /// leaves it a scheduled draft. Nothing is emailed when what was paid on account settles it. A draft with no
    /// address isn't issued: whoever scheduled it is told to send it. False when there was nothing to send.
    /// </summary>
    Task<bool> SendScheduledAsync(
        long invoiceId,
        Func<InvoiceResponse, string, CancellationToken, Task> email,
        CancellationToken cancellationToken = default);

    /// <summary>Tells whoever scheduled the draft, once, that it couldn't be sent and is tried again.</summary>
    Task ScheduledSendFailedAsync(long invoiceId, CancellationToken cancellationToken = default);
}
