using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Portal.Abstraction;

public interface IPortalInvitationRepository
{
    /// <summary>Tracked, with the account and the project loaded.</summary>
    Task<PortalInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with the account loaded.</summary>
    Task<PortalInvitation?> GetAsync(long invitationId, CancellationToken cancellationToken = default);

    /// <summary>The latest invitation of each account to the project, untracked.</summary>
    Task<IReadOnlyList<PortalInvitation>> LatestForProjectAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Tracked, with the project loaded: every invitation of the account that is still pending and unexpired, oldest first.</summary>
    Task<IReadOnlyList<PortalInvitation>> PendingForAccountAsync(long accountId, DateTime now, CancellationToken cancellationToken = default);

    Task<bool> HasPendingAsync(long accountId, long projectId, DateTime now, CancellationToken cancellationToken = default);

    void Add(PortalInvitation invitation);

    /// <summary>
    /// Marks the invitation accepted only if it is still pending and unexpired, in a single statement, so the same link
    /// can never be used twice.
    /// </summary>
    Task<bool> TryAcceptAsync(long invitationId, DateTime acceptedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the invitation only if it is neither accepted nor revoked, in a single statement, so it can't be revoked
    /// after the client accepted it at the same moment.
    /// </summary>
    Task<bool> TryRevokeAsync(long invitationId, DateTime revokedAt, CancellationToken cancellationToken = default);

    /// <summary>Revokes the account's pending invitations to the project.</summary>
    Task RevokePendingAsync(long accountId, long projectId, DateTime revokedAt, CancellationToken cancellationToken = default);

    /// <summary>Revokes every pending invitation to the project.</summary>
    Task RevokePendingForProjectAsync(long projectId, DateTime revokedAt, CancellationToken cancellationToken = default);
}
