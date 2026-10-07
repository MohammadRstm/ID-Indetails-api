using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Proton.Abstraction;

/// <summary>
/// Proton's proposed actions. Every status change is one conditional UPDATE that runs immediately, so two requests can
/// never both move the same action out of pending, and nothing else staged in the request is saved with it.
/// </summary>
public interface IProtonActionRepository
{
    /// <summary>Staged; saved with the unit of work.</summary>
    void Add(ProtonAction action);

    /// <summary>Not tracked.</summary>
    Task<ProtonAction?> FindAsync(Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>Marks the conversation's other pending proposals superseded.</summary>
    Task SupersedeAsync(Guid conversationId, Guid exceptPublicId, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Pending → executing for its own proposer before it expires. False when another request got there first.</summary>
    Task<bool> TryClaimAsync(Guid publicId, long userId, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Pending → <paramref name="status"/> (cancelled or expired). False when it was no longer pending.</summary>
    Task<bool> CloseAsync(Guid publicId, ProtonActionStatus status, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Executing → succeeded or failed, with what it produced or why it failed.</summary>
    Task FinishAsync(
        Guid publicId, ProtonActionStatus status, string? resultType, long? resultId, string? errorCode, string? errorMessage,
        DateTime at, CancellationToken cancellationToken = default);
}
