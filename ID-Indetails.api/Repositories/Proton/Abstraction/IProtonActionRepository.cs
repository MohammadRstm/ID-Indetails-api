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

    /// <summary>
    /// Marks the conversation's other pending proposals of the same tool for the same record superseded: a changed
    /// request replaces the card it changes, and other cards stay.
    /// </summary>
    Task SupersedeAsync(
        Guid conversationId, string tool, string? targetType, long? targetId, Guid exceptPublicId, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>
    /// The conversation's proposals still waiting for a Confirm (pending, not expired), apart from the one a new
    /// proposal for the same tool and target would replace.
    /// </summary>
    Task<int> CountPendingAsync(
        Guid conversationId, string tool, string? targetType, long? targetId, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>The proposals the user confirmed since <paramref name="since"/> (carried out, or tried).</summary>
    Task<int> CountConfirmedSinceAsync(long userId, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>How many of the user's <paramref name="tool"/> actions were carried out (or are running) since <paramref name="since"/>.</summary>
    Task<int> CountDoneSinceAsync(long userId, string tool, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>The user's own proposals, newest first: what Proton proposed and what became of each.</summary>
    Task<IReadOnlyList<ProtonAction>> MineAsync(long userId, int take, CancellationToken cancellationToken = default);

    /// <summary>Everyone's proposals, newest first, with who proposed them: for those who manage Proton.</summary>
    Task<IReadOnlyList<(ProtonAction Action, string Who)>> AllAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>Pending → executing for its own proposer before it expires. False when another request got there first.</summary>
    Task<bool> TryClaimAsync(Guid publicId, long userId, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Pending → <paramref name="status"/> (cancelled or expired). False when it was no longer pending.</summary>
    Task<bool> CloseAsync(Guid publicId, ProtonActionStatus status, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Executing → succeeded or failed, with what it produced or why it failed.</summary>
    Task FinishAsync(
        Guid publicId, ProtonActionStatus status, string? resultType, long? resultId, string? errorCode, string? errorMessage,
        DateTime at, CancellationToken cancellationToken = default);
}
