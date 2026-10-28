using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;

namespace ID_Indetails.api.Services.Billing.Abstraction;

/// <summary>Quotes: what a client project costs its client in all, TVA included. They need <c>invoice:send</c>.</summary>
public interface IQuoteService
{
    /// <summary>Makes a quote from the project's payment and TVA as they are now.</summary>
    Task<QuoteResponse> CreateAsync(long projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuoteResponse>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    Task<QuoteResponse> GetAsync(long quoteId, CancellationToken cancellationToken = default);

    /// <summary>Records a send (email, WhatsApp or a PDF download). Repeatable.</summary>
    Task<QuoteResponse> RecordDeliveryAsync(long quoteId, RecordDeliveryRequest request, CancellationToken cancellationToken = default);
}
