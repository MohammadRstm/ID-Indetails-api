using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Response.Billing;
using ID_Indetails.api.Services.Billing.Abstraction;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Each domain's invoice prefix, payment terms and bank details, and the company's exchange rates.</summary>
[ApiController]
[Route("api")]
public sealed class BillingSettingsController(IBillingSettingsService billingSettingsService) : ControllerBase
{
    [HttpGet("billing-settings/{domainCode}")]
    [HasAnyPermission(PermissionCodes.BillingSettingsManage, PermissionCodes.FinancialView)]
    public Task<BillingSettingsResponse> Get(string domainCode, CancellationToken cancellationToken)
        => billingSettingsService.GetAsync(domainCode, cancellationToken);

    /// <summary>Creates the settings the first time, replaces them afterwards.</summary>
    [HttpPut("billing-settings/{domainCode}")]
    [HasPermission(PermissionCodes.BillingSettingsManage)]
    public Task<BillingSettingsResponse> Save(string domainCode, SaveBillingSettingsRequest request, CancellationToken cancellationToken)
        => billingSettingsService.SaveAsync(domainCode, request, cancellationToken);

    /// <summary>A new dated rate for a currency. Company-wide.</summary>
    [HttpPost("exchange-rates")]
    [HasPermission(PermissionCodes.ExchangeRateManage)]
    public async Task<ActionResult<ExchangeRateResponse>> AddExchangeRate(AddExchangeRateRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await billingSettingsService.AddExchangeRateAsync(request, cancellationToken));

    /// <summary>Every dated rate, newest first per currency.</summary>
    [HttpGet("exchange-rates")]
    [HasPermission(PermissionCodes.FinancialView)]
    public Task<IReadOnlyList<ExchangeRateResponse>> ListExchangeRates([FromQuery] ExchangeRateListQuery query, CancellationToken cancellationToken)
        => billingSettingsService.ListExchangeRatesAsync(query, cancellationToken);
}
