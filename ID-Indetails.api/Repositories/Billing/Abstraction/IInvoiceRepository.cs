using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Billing.Abstraction;

public sealed record InvoiceSearch(
    IReadOnlyList<string> Domains,
    long? ProjectId,
    long? ClientId,
    InvoiceStatus? Status,
    bool? Overdue,
    string? Domain,
    DateOnly Today,
    int Page,
    int PageSize);

/// <summary>An invoice (with project and client loaded) and what its live payments add up to.</summary>
public sealed record InvoiceRow(Invoice Invoice, decimal Paid);

public interface IInvoiceRepository
{
    /// <summary>Newest first. Not tracked.</summary>
    Task<(IReadOnlyList<InvoiceRow> Items, int TotalCount)> SearchAsync(InvoiceSearch search, CancellationToken cancellationToken = default);

    /// <summary>Not tracked, with project, client, lines and deliveries.</summary>
    Task<InvoiceRow?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked with its lines, and locked (<c>SELECT … FOR UPDATE</c>) until the transaction ends.</summary>
    Task<Invoice?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>What the invoice's live (not voided) payments add up to.</summary>
    Task<decimal> PaidAsync(long invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the next invoice number of the domain and year. The sequence row stays locked until the
    /// transaction ends, so numbers are never handed out twice. Call inside a transaction.
    /// </summary>
    Task<int> NextSequenceAsync(string domainCode, int year, CancellationToken cancellationToken = default);

    void Add(Invoice invoice);

    void AddDelivery(InvoiceDelivery delivery);

    void Remove(Invoice invoice);
}
