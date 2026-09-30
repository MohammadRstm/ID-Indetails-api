using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.DTO.Response.Statements;

namespace ID_Indetails.api.Services.Documents.Abstraction;

public sealed record PdfFile(byte[] Content, string FileName);

/// <summary>
/// PDFs of invoices, statements and project reports, and emailing invoices and statements to the client. Who may
/// read or send one follows the services the figures come from.
/// </summary>
public interface IDocumentService
{
    /// <summary>Drafts too, marked as such.</summary>
    Task<PdfFile> InvoicePdfAsync(long invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Sends an issued invoice with its PDF and records the delivery. Nothing is recorded if sending fails.</summary>
    Task<InvoiceResponse> EmailInvoiceAsync(long invoiceId, EmailDocumentRequest request, CancellationToken cancellationToken = default);

    Task<PdfFile> StatementPdfAsync(long statementId, CancellationToken cancellationToken = default);

    /// <summary>Sends the statement with its PDF and records the delivery. Nothing is recorded if sending fails.</summary>
    Task<StatementResponse> EmailStatementAsync(long statementId, EmailDocumentRequest request, CancellationToken cancellationToken = default);

    /// <summary>A Design project's report, internal or for the client.</summary>
    Task<PdfFile> ProjectReportPdfAsync(long projectId, ProjectReportQuery query, CancellationToken cancellationToken = default);
}
