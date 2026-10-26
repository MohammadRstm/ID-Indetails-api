using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Billing.Abstraction;

/// <param name="IssuedFrom">Only invoices issued at or after this instant.</param>
/// <param name="IssuedBefore">Only invoices issued before this instant.</param>
public sealed record InvoiceSearch(
    IReadOnlyList<string> Domains,
    long? ProjectId,
    long? ClientId,
    InvoiceStatus? Status,
    bool? Overdue,
    string? Domain,
    DateOnly Today,
    int Page,
    int PageSize,
    DateTime? IssuedFrom = null,
    DateTime? IssuedBefore = null);

/// <summary>
/// An invoice (with project and client loaded), what its live payments add up to (in USD), and its total in USD: at the
/// rate in force on its issue day, or null for a draft, or when no rate was in force.
/// </summary>
public sealed record InvoiceRow(Invoice Invoice, decimal Paid, decimal? TotalUsd);

public interface IInvoiceRepository
{
    /// <summary>Newest first. Not tracked.</summary>
    Task<(IReadOnlyList<InvoiceRow> Items, int TotalCount)> SearchAsync(InvoiceSearch search, CancellationToken cancellationToken = default);

    /// <summary>Not tracked, with project, client, lines and deliveries.</summary>
    Task<InvoiceRow?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked with its lines, and locked (<c>SELECT … FOR UPDATE</c>) until the transaction ends.</summary>
    Task<Invoice?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The project's invoice, tracked (without lines) and locked; null for any other project's invoice.</summary>
    Task<Invoice?> GetForProjectForUpdateAsync(long projectId, long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the invoice's live (not voided) payments add up to, read with a locking read so it
    /// sees payments committed by other transactions. Call inside a transaction, after locking the invoice.
    /// </summary>
    Task<decimal> PaidForUpdateAsync(long invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the next invoice number of the domain and year. The sequence row stays locked until the
    /// transaction ends, so numbers are never handed out twice. Call inside a transaction.
    /// </summary>
    Task<int> NextSequenceAsync(string domainCode, int year, CancellationToken cancellationToken = default);

    /// <summary>Scheduled drafts whose day is <paramref name="dueBy"/> or earlier, the earliest first.</summary>
    Task<IReadOnlyList<long>> ScheduledDueAsync(DateOnly dueBy, CancellationToken cancellationToken = default);

    void Add(Invoice invoice);

    void AddDelivery(InvoiceDelivery delivery);

    void Remove(Invoice invoice);
}
