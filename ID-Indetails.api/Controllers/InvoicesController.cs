using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Services.Billing.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ID_Indetails.api.Controllers;

/// <summary>Invoices. Invoices of a project in a domain the caller can't see read as 404.</summary>
[ApiController]
[Route("api")]
public sealed class InvoicesController(IInvoiceService invoiceService) : ControllerBase
{
    [HttpGet("invoices")]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<PagedResponse<InvoiceSummaryResponse>> List([FromQuery] InvoiceListQuery query, CancellationToken cancellationToken)
        => invoiceService.ListAsync(query, cancellationToken);

    [HttpGet("invoices/{invoiceId:long}")]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<InvoiceResponse> Get(long invoiceId, CancellationToken cancellationToken)
        => invoiceService.GetAsync(invoiceId, cancellationToken);

    /// <summary>A new draft for the project's client.</summary>
    [HttpPost("projects/{projectId:long}/invoices")]
    [HasPermission(PermissionCodes.InvoiceCreate)]
    public async Task<ActionResult<InvoiceResponse>> Create(long projectId, SaveInvoiceRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await invoiceService.CreateAsync(projectId, request, cancellationToken));

    /// <summary>Drafts only.</summary>
    [HttpPut("invoices/{invoiceId:long}")]
    [HasPermission(PermissionCodes.InvoiceCreate)]
    public Task<InvoiceResponse> Update(long invoiceId, SaveInvoiceRequest request, CancellationToken cancellationToken)
        => invoiceService.UpdateAsync(invoiceId, request, cancellationToken);

    /// <summary>Discards a draft.</summary>
    [HttpDelete("invoices/{invoiceId:long}")]
    [HasPermission(PermissionCodes.InvoiceCreate)]
    public async Task<IActionResult> Discard(long invoiceId, CancellationToken cancellationToken)
    {
        await invoiceService.DiscardAsync(invoiceId, cancellationToken);
        return NoContent();
    }

    /// <summary>Numbers a draft and sets its due date. The body is optional (issued today).</summary>
    [HttpPost("invoices/{invoiceId:long}/issue")]
    [HasPermission(PermissionCodes.InvoiceSend)]
    public Task<InvoiceResponse> Issue(
        long invoiceId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] IssueInvoiceRequest? request,
        CancellationToken cancellationToken)
        => invoiceService.IssueAsync(invoiceId, request ?? new IssueInvoiceRequest(), cancellationToken);

    /// <summary>Records that an issued invoice was sent or downloaded. Can be repeated.</summary>
    [HttpPost("invoices/{invoiceId:long}/deliveries")]
    [HasPermission(PermissionCodes.InvoiceSend)]
    public async Task<ActionResult<InvoiceResponse>> RecordDelivery(long invoiceId, RecordDeliveryRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await invoiceService.RecordDeliveryAsync(invoiceId, request, cancellationToken));

    [HttpPost("invoices/{invoiceId:long}/void")]
    [HasPermission(PermissionCodes.InvoiceVoid)]
    public Task<InvoiceResponse> Void(long invoiceId, VoidRequest request, CancellationToken cancellationToken)
        => invoiceService.VoidAsync(invoiceId, request, cancellationToken);
}
