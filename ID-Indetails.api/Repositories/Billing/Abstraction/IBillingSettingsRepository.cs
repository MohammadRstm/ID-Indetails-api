using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Billing.Abstraction;

public interface IBillingSettingsRepository
{
    /// <summary>Not tracked.</summary>
    Task<BillingSettings?> GetAsync(string domainCode, CancellationToken cancellationToken = default);

    /// <summary>Tracked.</summary>
    Task<BillingSettings?> GetForUpdateAsync(string domainCode, CancellationToken cancellationToken = default);

    /// <summary>Whether another domain already numbers its invoices with this prefix.</summary>
    Task<bool> PrefixUsedElsewhereAsync(string prefix, string domainCode, CancellationToken cancellationToken = default);

    void Add(BillingSettings settings);
}
