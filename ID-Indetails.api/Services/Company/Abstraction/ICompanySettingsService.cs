using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Request.Files;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Files.Abstraction;

namespace ID_Indetails.api.Services.Company.Abstraction;

public interface ICompanySettingsService
{
    Task<CompanySettingsResponse> GetAsync(CancellationToken cancellationToken = default);

    Task<CompanySettingsResponse> UpdateAsync(UpdateCompanySettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets the logo from the caller's upload; the settings must be saved first.</summary>
    Task<CompanySettingsResponse> SetLogoAsync(AttachFileRequest request, CancellationToken cancellationToken = default);

    Task<CompanySettingsResponse> RemoveLogoAsync(CancellationToken cancellationToken = default);

    /// <summary>For the sign-in page, before anyone signs in.</summary>
    Task<BrandingResponse> BrandingAsync(CancellationToken cancellationToken = default);

    /// <summary>The logo's image, for anyone; not found when there is none.</summary>
    Task<FileContent> OpenLogoAsync(CancellationToken cancellationToken = default);
}
