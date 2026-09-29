using ID_Indetails.api.DTO.Request.Auth;

namespace ID_Indetails.api.Services.Auth.Abstraction;

/// <summary>Resetting a forgotten password through an emailed link.</summary>
public interface IPasswordResetService
{
    /// <summary>Emails a reset link to the active account with this email, if there is one. Reveals nothing either way.</summary>
    Task RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets the new password from a reset link, which then stops working, and signs the account out everywhere.</summary>
    Task ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
