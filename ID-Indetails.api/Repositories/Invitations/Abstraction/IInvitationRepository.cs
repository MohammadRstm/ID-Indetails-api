using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Invitations.Abstraction;

public interface IInvitationRepository
{
    /// <summary>Tracked, with the invited user and their team loaded.</summary>
    Task<UserInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Stages a new invitation; saved by the unit of work.</summary>
    void Add(UserInvitation invitation);

    /// <summary>
    /// Marks the invitation accepted only if it is still pending and unexpired, in a single
    /// statement, so the same link can never be accepted twice.
    /// </summary>
    Task<bool> TryAcceptAsync(long invitationId, DateTime acceptedAt, CancellationToken cancellationToken = default);

    /// <summary>Revokes every pending invitation of a user (used when resending or accepting).</summary>
    Task RevokePendingForUserAsync(long userId, DateTime revokedAt, CancellationToken cancellationToken = default);
}
