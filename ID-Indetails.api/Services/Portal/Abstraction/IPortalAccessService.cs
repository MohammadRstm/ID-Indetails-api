using ID_Indetails.api.DTO.Request.Portal;
using ID_Indetails.api.DTO.Response.Portal;

namespace ID_Indetails.api.Services.Portal.Abstraction;

/// <summary>The staff side: who of the client's contacts can follow a Design project, invitations and ending access.</summary>
public interface IPortalAccessService
{
    Task<PortalClientsResponse> ListAsync(long projectId, CancellationToken cancellationToken = default);

    Task<PortalClientsResponse> InviteAsync(long projectId, InvitePortalClientRequest request, CancellationToken cancellationToken = default);

    Task<PortalClientsResponse> ResendAsync(long projectId, long invitationId, CancellationToken cancellationToken = default);

    Task<PortalClientsResponse> RevokeInvitationAsync(long projectId, long invitationId, CancellationToken cancellationToken = default);

    Task<PortalClientsResponse> RevokeAccessAsync(long projectId, long accessId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends every client's access to the project and cancels its pending invitations, when the project moves to another
    /// client or becomes internal. Staged in the caller's unit of work and transaction.
    /// </summary>
    Task EndAllForProjectAsync(long projectId, string domainCode, long? previousClientId, long actorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same for every project of the client, when the client is archived; restoring it gives nothing back. Saved in
    /// the caller's unit of work, inside its transaction.
    /// </summary>
    Task EndAllForClientAsync(long clientId, string domainCode, long actorId, CancellationToken cancellationToken = default);
}
