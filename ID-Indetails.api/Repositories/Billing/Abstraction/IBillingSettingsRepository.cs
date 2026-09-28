using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Billing.Abstraction;

public interface IBillingSettingsRepository
{
    /// <summary>Not tracked.</summary>
    Task<BillingSettings?> GetAsync(string domainCode, CancellationToken cancellationToken = default);

    /// <summary>Tracked.</summary>
    Task<BillingSettings?> GetForUpdateAsync(string domainCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether another domain uses this prefix now, or used it on invoices it already numbered
    /// (reusing it would collide with those numbers).
    /// </summary>
    Task<bool> PrefixUsedElsewhereAsync(string prefix, string domainCode, CancellationToken cancellationToken = default);

    /// <summary>Serialises billing-settings saves until the transaction ends, so two domains can't take the same prefix at once.</summary>
    Task LockAllAsync(CancellationToken cancellationToken = default);

    void Add(BillingSettings settings);
}
