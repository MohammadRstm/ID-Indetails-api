using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Billing.Abstraction;

public interface IPaymentService
{
    Task<PagedResponse<PaymentResponse>> ListAsync(PaymentListQuery query, CancellationToken cancellationToken = default);

    Task<PaymentResponse> GetAsync(long paymentId, CancellationToken cancellationToken = default);

    /// <summary>On account, or against an issued invoice of the project. No invoice is ever required.</summary>
    Task<PaymentResponse> RecordAsync(long projectId, RecordPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a payment for a project the caller already holds (tracked), inside the caller's
    /// transaction. Used when a project is created with its initial payment.
    /// </summary>
    Task<Payment> RecordForProjectAsync(Project project, RecordPaymentRequest request, long actorId, CancellationToken cancellationToken = default);

    /// <summary>Ties a payment on account to an issued invoice of the same project.</summary>
    Task<PaymentResponse> ApplyAsync(long paymentId, ApplyPaymentRequest request, CancellationToken cancellationToken = default);

    Task<PaymentResponse> VoidAsync(long paymentId, VoidRequest request, CancellationToken cancellationToken = default);
}
