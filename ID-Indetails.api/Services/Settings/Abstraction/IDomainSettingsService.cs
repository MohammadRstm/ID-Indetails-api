using ID_Indetails.api.DTO.Request.Settings;
using ID_Indetails.api.DTO.Response.Settings;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Settings.Abstraction;

public interface IDomainSettingsService
{
    Task<DomainSettingsResponse> GetAsync(string domainCode, CancellationToken cancellationToken = default);

    Task<DomainSettingsResponse> SaveAsync(string domainCode, SaveDomainSettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>The domain's settings, or the defaults when none were saved. No access check: for other services.</summary>
    Task<DomainSettings> CurrentAsync(string domainCode, CancellationToken cancellationToken = default);
}
