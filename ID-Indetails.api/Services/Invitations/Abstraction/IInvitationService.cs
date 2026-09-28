using ID_Indetails.api.DTO.Request.Invitations;
using ID_Indetails.api.DTO.Response.Invitations;
using ID_Indetails.api.Services.Auth.Abstraction;

namespace ID_Indetails.api.Services.Invitations.Abstraction;

public interface IInvitationService
{
    /// <summary>Creates the invited user and emails them a one-time signup link.</summary>
    Task<InvitationResponse> CreateAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Cancels the user's pending link and emails a new one.</summary>
    Task<InvitationResponse> ResendAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>What the signup page shows. Fails the same way for unknown, used, revoked or expired links.</summary>
    Task<InvitationPreviewResponse> PreviewAsync(InvitationTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>Completes signup and signs the new user in.</summary>
    Task<AuthSession> AcceptAsync(AcceptInvitationRequest request, CancellationToken cancellationToken = default);
}
