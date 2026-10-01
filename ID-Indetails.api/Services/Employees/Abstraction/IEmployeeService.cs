using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Request.Files;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Employees;

namespace ID_Indetails.api.Services.Employees.Abstraction;

/// <summary>People management (see <c>Business/Employees/EmployeeRules</c>).</summary>
public interface IEmployeeService
{
    Task<PagedResponse<EmployeeSummaryResponse>> ListAsync(EmployeeListQuery query, CancellationToken cancellationToken = default);

    Task<EmployeeResponse> GetAsync(long userId, CancellationToken cancellationToken = default);

    Task<EmployeeResponse> UpdateAsync(long userId, UpdateEmployeeRequest request, CancellationToken cancellationToken = default);

    Task<EmployeeResponse> DeactivateAsync(long userId, DeactivateEmployeeRequest? request, CancellationToken cancellationToken = default);

    Task<EmployeeResponse> ReactivateAsync(long userId, ReactivateEmployeeRequest? request, CancellationToken cancellationToken = default);

    /// <summary>Signs them out everywhere at once and blocks their sign-in until <see cref="RestoreSignInAsync"/>.</summary>
    Task<EmployeeResponse> EndSessionsAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Lets someone whose sessions were ended sign in again. Their old sessions stay ended.</summary>
    Task<EmployeeResponse> RestoreSignInAsync(long userId, CancellationToken cancellationToken = default);

    Task<EmployeeResponse> ChangeRoleAsync(long userId, ChangeRoleRequest request, CancellationToken cancellationToken = default);

    Task<EmployeeResponse> MoveAsync(long userId, MoveEmployeeRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayResponse>> GetPayHistoryAsync(long userId, CancellationToken cancellationToken = default);

    Task<PayResponse> ChangePayAsync(long userId, ChangePayRequest request, CancellationToken cancellationToken = default);

    /// <summary>The signed-in user's own name and phone.</summary>
    Task<CurrentUserResponse> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets the caller's profile photo from their upload.</summary>
    Task<CurrentUserResponse> SetMyPhotoAsync(AttachFileRequest request, CancellationToken cancellationToken = default);

    Task<CurrentUserResponse> RemoveMyPhotoAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets someone's profile photo from the caller's upload.</summary>
    Task<EmployeeResponse> SetPhotoAsync(long userId, AttachFileRequest request, CancellationToken cancellationToken = default);

    Task<EmployeeResponse> RemovePhotoAsync(long userId, CancellationToken cancellationToken = default);
}
