using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Documents.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>Client payments. Payments of a project in a domain the caller can't see read as 404.</summary>
[ApiController]
[Route("api")]
public sealed class PaymentsController(IPaymentService paymentService, IDocumentService documents) : ControllerBase
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

    /// <summary>The payment's receipt as a PDF: the payment, and where the project stands by it. Downloading it records nothing.</summary>
    [HttpGet("payments/{paymentId:long}/receipt")]
    [HasPermission(PermissionCodes.PaymentRecord)]
    [EnableRateLimiting(RateLimitPolicies.Export)]
    public async Task<IActionResult> Receipt(long paymentId, CancellationToken cancellationToken)
    {
        var pdf = await documents.ReceiptPdfAsync(paymentId, cancellationToken);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }

    /// <summary>Emails the payment's receipt to the client with its PDF attached, and notes it in the project's activity.</summary>
    [HttpPost("payments/{paymentId:long}/receipt/email")]
    [HasPermission(PermissionCodes.PaymentRecord)]
    [EnableRateLimiting(RateLimitPolicies.Email)]
    public async Task<IActionResult> EmailReceipt(long paymentId, EmailReceiptRequest request, CancellationToken cancellationToken)
    {
        await documents.EmailReceiptAsync(paymentId, request, cancellationToken);
        return NoContent();
    }

    [HttpPost("payments/{paymentId:long}/void")]
    [HasPermission(PermissionCodes.PaymentVoid)]
    public Task<PaymentResponse> Void(long paymentId, VoidRequest request, CancellationToken cancellationToken)
        => paymentService.VoidAsync(paymentId, request, cancellationToken);
}
