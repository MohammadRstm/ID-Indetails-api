using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.RateLimiting;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Documents.Abstraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ID_Indetails.api.Controllers;

/// <summary>Quotes: what a client project costs its client in all, TVA included and never shown apart.</summary>
[ApiController]
[Route("api")]
[HasPermission(PermissionCodes.InvoiceSend)]
public sealed class QuotesController(IQuoteService quotes, IDocumentService documents) : ControllerBase
{
    /// <summary>Makes a quote from the project's payment and TVA as they are now.</summary>
    [HttpPost("projects/{projectId:long}/quotes")]
    public async Task<ActionResult<QuoteResponse>> Create(long projectId, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await quotes.CreateAsync(projectId, cancellationToken));

    /// <summary>The project's quotes, newest first.</summary>
    [HttpGet("projects/{projectId:long}/quotes")]
    public Task<IReadOnlyList<QuoteResponse>> List(long projectId, CancellationToken cancellationToken)
        => quotes.ListAsync(projectId, cancellationToken);

    [HttpGet("quotes/{quoteId:long}")]
    public Task<QuoteResponse> Get(long quoteId, CancellationToken cancellationToken)
        => quotes.GetAsync(quoteId, cancellationToken);

    /// <summary>Records a send (WhatsApp or a PDF download). Repeatable.</summary>
    [HttpPost("quotes/{quoteId:long}/deliveries")]
    public Task<QuoteResponse> RecordDelivery(long quoteId, RecordDeliveryRequest request, CancellationToken cancellationToken)
        => quotes.RecordDeliveryAsync(quoteId, request, cancellationToken);

    [HttpGet("quotes/{quoteId:long}/pdf")]
    [EnableRateLimiting(RateLimitPolicies.Export)]
    public async Task<IActionResult> Pdf(long quoteId, CancellationToken cancellationToken)
    {
        var pdf = await documents.QuotePdfAsync(quoteId, cancellationToken);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }

    /// <summary>Emails the quote to the client with its PDF attached, and records the delivery.</summary>
    [HttpPost("quotes/{quoteId:long}/email")]
    [EnableRateLimiting(RateLimitPolicies.Email)]
    public Task<QuoteResponse> Email(long quoteId, EmailDocumentRequest request, CancellationToken cancellationToken)
        => documents.EmailQuoteAsync(quoteId, request, cancellationToken);
}
