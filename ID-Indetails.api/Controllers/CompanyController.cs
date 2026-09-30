using ID_Indetails.api.Authorization;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Request.Files;
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

    /// <summary>The logo, from the caller's upload.</summary>
    [HttpPut("logo")]
    [HasPermission(PermissionCodes.CompanySettingsManage)]
    public Task<CompanySettingsResponse> SetLogo(AttachFileRequest request, CancellationToken cancellationToken)
        => settings.SetLogoAsync(request, cancellationToken);

    [HttpDelete("logo")]
    [HasPermission(PermissionCodes.CompanySettingsManage)]
    public Task<CompanySettingsResponse> RemoveLogo(CancellationToken cancellationToken)
        => settings.RemoveLogoAsync(cancellationToken);

    /// <summary>The company's name and whether it has a logo, for the sign-in page. No sign-in needed.</summary>
    [HttpGet("branding")]
    [AllowAnonymous]
    public Task<BrandingResponse> Branding(CancellationToken cancellationToken) => settings.BrandingAsync(cancellationToken);

    /// <summary>The logo's image, for the sign-in page. No sign-in needed.</summary>
    [HttpGet("logo")]
    [AllowAnonymous]
    public async Task<IActionResult> Logo(CancellationToken cancellationToken)
    {
        var logo = await settings.OpenLogoAsync(cancellationToken);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(logo.Content, logo.MimeType);
    }

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
