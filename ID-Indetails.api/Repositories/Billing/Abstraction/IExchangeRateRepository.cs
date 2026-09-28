using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Billing.Abstraction;

public interface IExchangeRateRepository
{
    /// <summary>Newest first, for one currency or all of them.</summary>
    Task<IReadOnlyList<ExchangeRate>> ListAsync(string? currency, CancellationToken cancellationToken = default);

    /// <summary>The rate in force on <paramref name="day"/>: the latest one effective on or before it.</summary>
    Task<ExchangeRate?> GetInForceAsync(string currency, DateOnly day, CancellationToken cancellationToken = default);
}
