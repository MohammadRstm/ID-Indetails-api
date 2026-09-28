using ID_Indetails.api.DTO.Request.Setup;
using ID_Indetails.api.DTO.Response.Setup;

namespace ID_Indetails.api.Services.Setup.Abstraction;

public interface ISetupService
{
    /// <summary>
    /// Creates a super admin with a generated password and emails it to them. Callers must
    /// already have checked the setup key (see <c>RequireSetupKeyAttribute</c>).
    /// </summary>
    Task<SuperAdminCreatedResponse> CreateSuperAdminAsync(
        CreateSuperAdminRequest request,
        CancellationToken cancellationToken = default);
}
