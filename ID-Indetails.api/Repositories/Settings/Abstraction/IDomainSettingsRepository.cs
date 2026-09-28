using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Settings.Abstraction;

public interface IDomainSettingsRepository
{
    /// <summary>Not tracked. Null until the domain's admins first save their settings.</summary>
    Task<DomainSettings?> GetAsync(string domainCode, CancellationToken cancellationToken = default);

    /// <summary>Tracked, and locked (<c>SELECT … FOR UPDATE</c>) until the transaction ends. Call inside a transaction.</summary>
    Task<DomainSettings?> GetForUpdateAsync(string domainCode, CancellationToken cancellationToken = default);

    void Add(DomainSettings settings);
}
