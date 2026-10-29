using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Billing.Abstraction;

/// <summary>A quote with its project, client and deliveries, and the names of who made and sent it.</summary>
public sealed record QuoteRow(Quote Quote, string Domain, string ProjectTitle, string? ProjectLocation, string ClientName, IReadOnlyDictionary<long, string> People);

public interface IQuoteRepository
{
    /// <summary>The next number of the year, taken atomically.</summary>
    Task<int> NextSequenceAsync(int year, CancellationToken cancellationToken = default);

    Task<QuoteRow?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The project's quotes, newest first.</summary>
    Task<IReadOnlyList<QuoteRow>> ListAsync(long projectId, CancellationToken cancellationToken = default);

    void Add(Quote quote);

    void AddDelivery(QuoteDelivery delivery);
}
