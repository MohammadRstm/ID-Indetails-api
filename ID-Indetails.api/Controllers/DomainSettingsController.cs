using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Settings;
using ID_Indetails.api.DTO.Response.Settings;
using ID_Indetails.api.Services.Settings.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Each domain's time-tracking and notification rules.</summary>
[ApiController]
[Route("api/domain-settings")]
public sealed class DomainSettingsController(IDomainSettingsService domainSettingsService) : ControllerBase
{
    /// <summary>Readable by anyone of the domain: employees need the entry rules too.</summary>
    [HttpGet("{domainCode}")]
    [Authorize]
    public Task<DomainSettingsResponse> Get(string domainCode, CancellationToken cancellationToken)
        => domainSettingsService.GetAsync(domainCode, cancellationToken);

    /// <summary>Creates the settings the first time, replaces them afterwards.</summary>
    [HttpPut("{domainCode}")]
    [HasPermission(PermissionCodes.DomainSettingsManage)]
    public Task<DomainSettingsResponse> Save(string domainCode, SaveDomainSettingsRequest request, CancellationToken cancellationToken)
        => domainSettingsService.SaveAsync(domainCode, request, cancellationToken);
}
