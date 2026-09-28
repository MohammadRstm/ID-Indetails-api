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
}
