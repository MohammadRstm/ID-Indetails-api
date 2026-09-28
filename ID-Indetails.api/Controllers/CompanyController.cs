using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Services.Company.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ID_Indetails.api.Controllers;

/// <summary>Company settings and the company working week.</summary>
[ApiController]
[Route("api/company")]
public sealed class CompanyController(ICompanySettingsService settings, IWorkingTimeService workingTime) : ControllerBase
{
    /// <summary>Name, week start, time format and default currency; every page needs them.</summary>
    [HttpGet("settings")]
    [Authorize]
    public Task<CompanySettingsResponse> GetSettings(CancellationToken cancellationToken)
        => settings.GetAsync(cancellationToken);

    [HttpPut("settings")]
    [HasPermission(PermissionCodes.CompanySettingsManage)]
    public Task<CompanySettingsResponse> UpdateSettings(UpdateCompanySettingsRequest request, CancellationToken cancellationToken)
        => settings.UpdateAsync(request, cancellationToken);

    /// <summary>The policy in force today and every dated change.</summary>
    [HttpGet("working-time")]
    [Authorize]
    public Task<WorkingTimeResponse> GetWorkingTime(CancellationToken cancellationToken)
        => workingTime.GetAsync(cancellationToken);

    /// <summary>A new working week, break and overtime multiplier from today or a later date.</summary>
    [HttpPost("working-time")]
    [HasPermission(PermissionCodes.WorkingTimeManage)]
    public Task<WorkingTimeResponse> SetWorkingTime(SetWorkingTimeRequest request, CancellationToken cancellationToken)
        => workingTime.SetAsync(request, cancellationToken);
}
