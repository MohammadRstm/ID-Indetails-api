using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Services.Billing.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Client payments. Payments of a project in a domain the caller can't see read as 404.</summary>
[ApiController]
[Route("api")]
public sealed class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    [HttpGet("payments")]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<PagedResponse<PaymentResponse>> List([FromQuery] PaymentListQuery query, CancellationToken cancellationToken)
        => paymentService.ListAsync(query, cancellationToken);

    [HttpGet("payments/{paymentId:long}")]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<PaymentResponse> Get(long paymentId, CancellationToken cancellationToken)
        => paymentService.GetAsync(paymentId, cancellationToken);

    /// <summary>On account, or against an issued invoice of the project.</summary>
    [HttpPost("projects/{projectId:long}/payments")]
    [HasPermission(PermissionCodes.PaymentRecord)]
    public async Task<ActionResult<PaymentResponse>> Record(long projectId, RecordPaymentRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await paymentService.RecordAsync(projectId, request, cancellationToken));

    /// <summary>Applies a payment on account to an issued invoice.</summary>
    [HttpPut("payments/{paymentId:long}/invoice")]
    [HasPermission(PermissionCodes.PaymentRecord)]
    public Task<PaymentResponse> Apply(long paymentId, ApplyPaymentRequest request, CancellationToken cancellationToken)
        => paymentService.ApplyAsync(paymentId, request, cancellationToken);

    /// <summary>Files a payment under an account label, or clears it. The money of a payment is never edited.</summary>
    [HttpPut("payments/{paymentId:long}/label")]
    [HasPermission(PermissionCodes.PaymentRecord)]
    public Task<PaymentResponse> SetLabel(long paymentId, SetPaymentLabelRequest request, CancellationToken cancellationToken)
        => paymentService.SetLabelAsync(paymentId, request, cancellationToken);

    [HttpPost("payments/{paymentId:long}/void")]
    [HasPermission(PermissionCodes.PaymentVoid)]
    public Task<PaymentResponse> Void(long paymentId, VoidRequest request, CancellationToken cancellationToken)
        => paymentService.VoidAsync(paymentId, request, cancellationToken);
}
