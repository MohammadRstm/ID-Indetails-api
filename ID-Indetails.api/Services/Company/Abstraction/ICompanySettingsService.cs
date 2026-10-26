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
    /// <summary>The company's own address, which the website shows and its contact form sends to. Empty clears it.</summary>
    Task<CompanySettingsResponse> SetContactEmailAsync(CompanyContactEmailRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lets Proton read project files, or stops it: reading one sends its content to Anthropic.</summary>
    Task<CompanySettingsResponse> SetProtonFilesAsync(CompanyProtonSwitchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lets Proton suggest replies in the discussions with clients, for everyone, or stops it.</summary>
    Task<CompanySettingsResponse> SetProtonSuggestionsAsync(CompanyProtonSwitchRequest request, CancellationToken cancellationToken = default);

    Task<CompanySettingsResponse> SetLogoAsync(AttachFileRequest request, CancellationToken cancellationToken = default);

    Task<CompanySettingsResponse> RemoveLogoAsync(CancellationToken cancellationToken = default);

    /// <summary>For the sign-in page, before anyone signs in.</summary>
    Task<BrandingResponse> BrandingAsync(CancellationToken cancellationToken = default);

    /// <summary>The logo's image, for anyone; not found when there is none.</summary>
    Task<FileContent> OpenLogoAsync(CancellationToken cancellationToken = default);
}
