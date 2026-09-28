using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Company.Abstraction;

public interface ICompanySettingsService
{
    Task<CompanySettingsResponse> GetAsync(CancellationToken cancellationToken = default);

    Task<CompanySettingsResponse> UpdateAsync(UpdateCompanySettingsRequest request, CancellationToken cancellationToken = default);
}
