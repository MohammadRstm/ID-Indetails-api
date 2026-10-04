using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.Services.Auth.Abstraction;

namespace ID_Indetails.api.Services.Employees.Abstraction;

/// <summary>Changing a sign-in address (<c>employee:change_email</c>): someone else's, or one's own with one's password.</summary>
public interface ISignInEmailService
{
    Task<EmployeeResponse> ChangeAsync(long userId, ChangeEmailRequest request, CancellationToken cancellationToken = default);

    /// <summary>Their own; the session they are using is replaced with a new one.</summary>
    Task<AuthSession> ChangeMineAsync(ChangeMyEmailRequest request, CancellationToken cancellationToken = default);
}
