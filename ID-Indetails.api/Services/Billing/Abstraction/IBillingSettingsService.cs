using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;

namespace ID_Indetails.api.Services.Billing.Abstraction;

public interface IBillingSettingsService
{
    Task<BillingSettingsResponse> GetAsync(string domainCode, CancellationToken cancellationToken = default);

    Task<BillingSettingsResponse> SaveAsync(string domainCode, SaveBillingSettingsRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExchangeRateResponse>> ListExchangeRatesAsync(ExchangeRateListQuery query, CancellationToken cancellationToken = default);
}
