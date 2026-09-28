using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Billing.Implementation;

public sealed class ExchangeRateRepository(IdIndetailsDbContext db) : IExchangeRateRepository
{
    public async Task<IReadOnlyList<ExchangeRate>> ListAsync(string? currency, CancellationToken cancellationToken = default)
        => await db.ExchangeRates
            .AsNoTracking()
            .Where(r => currency == null || r.Currency == currency)
            .OrderBy(r => r.Currency)
            .ThenByDescending(r => r.EffectiveFrom)
            .ToListAsync(cancellationToken);

    public Task<ExchangeRate?> GetInForceAsync(string currency, DateOnly day, CancellationToken cancellationToken = default)
        => db.ExchangeRates
            .AsNoTracking()
            .Where(r => r.Currency == currency && r.EffectiveFrom <= day)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<DateOnly?> GetLatestDateAsync(string currency, CancellationToken cancellationToken = default)
        => db.ExchangeRates
            .Where(r => r.Currency == currency)
            .MaxAsync(r => (DateOnly?)r.EffectiveFrom, cancellationToken);

    public void Add(ExchangeRate rate) => db.ExchangeRates.Add(rate);
}
