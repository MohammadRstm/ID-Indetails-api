using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.DTO.Response.Boqs;

namespace ID_Indetails.api.Services.Documents.Abstraction;

public sealed record PdfFile(byte[] Content, string FileName);

/// <summary>
/// PDFs of invoices, quotes, BOQs and project reports, and emailing invoices, quotes and BOQs to the client. Who may
/// read or send one follows the services the figures come from.
/// </summary>
public interface IDocumentService
{
    /// <summary>Drafts too, marked as such.</summary>
    Task<PdfFile> InvoicePdfAsync(long invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Sends an issued invoice with its PDF and records the delivery. Nothing is recorded if sending fails.</summary>
    Task<InvoiceResponse> EmailInvoiceAsync(long invoiceId, EmailDocumentRequest request, CancellationToken cancellationToken = default);

    /// <summary>A reminder of an overdue invoice, from the company's accounts, with the invoice attached. Records nothing.</summary>
    Task EmailInvoiceReminderAsync(InvoiceResponse invoice, string to, CancellationToken cancellationToken = default);

    /// <summary>A scheduled invoice on its day, signed by whoever scheduled it. Records nothing: the scheduled sending does.</summary>
    Task EmailScheduledInvoiceAsync(InvoiceResponse invoice, string to, CancellationToken cancellationToken = default);

    /// <summary>A client BOQ or VO; an internal draft has none.</summary>
    Task<PdfFile> BoqPdfAsync(long boqId, CancellationToken cancellationToken = default);

    /// <summary>Sends the client BOQ or VO with its PDF and records the delivery. Nothing is recorded if sending fails.</summary>
    Task<BoqDetailResponse> EmailBoqAsync(long boqId, EmailBoqRequest request, CancellationToken cancellationToken = default);

    Task<PdfFile> QuotePdfAsync(long quoteId, CancellationToken cancellationToken = default);

    /// <summary>Emails the quote to the client with its PDF attached, and records the delivery.</summary>
    Task<QuoteResponse> EmailQuoteAsync(long quoteId, EmailDocumentRequest request, CancellationToken cancellationToken = default);

    /// <summary>A Design project's report, internal or for the client.</summary>
    Task<PdfFile> ProjectReportPdfAsync(long projectId, ProjectReportQuery query, CancellationToken cancellationToken = default);
}
