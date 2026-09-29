using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Billing.Abstraction;

public sealed record PaymentSearch(
    IReadOnlyList<string> Domains,
    long? ProjectId,
    long? ClientId,
    long? InvoiceId,
    bool IncludeVoided,
    string? Domain,
    int Page,
    int PageSize);

public interface IPaymentRepository
{
    /// <summary>Most recently received first, with project, client and invoice. Not tracked.</summary>
    Task<(IReadOnlyList<Payment> Items, int TotalCount)> SearchAsync(PaymentSearch search, CancellationToken cancellationToken = default);

    /// <summary>Not tracked, with project, client and invoice.</summary>
    Task<Payment?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, and locked (<c>SELECT … FOR UPDATE</c>) until the transaction ends.</summary>
    Task<Payment?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    Task<bool> HasLiveInitialAsync(long projectId, CancellationToken cancellationToken = default);

    void Add(Payment payment);
}
