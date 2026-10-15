using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>The client's side: invitation links, sign-in, sessions and password resets.</summary>
public interface IPortalAuthService
{
    Task<PortalInvitationPreviewResponse> PreviewInvitationAsync(PortalInvitationTokenRequest request, CancellationToken cancellationToken = default);

    Task<PortalAuthSession> AcceptInvitationAsync(AcceptPortalInvitationRequest request, CancellationToken cancellationToken = default);

    Task<PortalAuthSession> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<PortalAuthSession> RefreshAsync(string? refreshCookieValue, CancellationToken cancellationToken = default);

    Task LogoutAsync(string? refreshCookieValue, CancellationToken cancellationToken = default);

    Task<PortalAccountResponse> GetAccountAsync(long accountId, CancellationToken cancellationToken = default);

    Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
