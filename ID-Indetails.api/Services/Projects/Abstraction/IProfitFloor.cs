using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Projects.Abstraction;

/// <summary>
/// A client project's profit may not reach zero. A change that would take it there is refused and kept for forcing;
/// one that takes it close to zero alerts whoever sees the money, and asks an Execution site's pocket money for help.
/// </summary>
public interface IProfitFloor
{
    /// <summary>True while a blocked change is being forced through: the floor lets it pass.</summary>
    bool Forcing { get; set; }

    /// <summary>The project's profit before a change, with the project locked: what <see cref="CheckAsync{T}"/> compares with.</summary>
    Task<ProfitBefore> BeforeAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Within the caller's transaction, after its change is saved: throws <see cref="ProfitFloorException"/> when the
    /// change lowered the project's profit to zero or below (unless forcing), and alerts when it lowered it close to
    /// zero. A change that raises the profit always passes, so a project below zero can still be helped.
    /// </summary>
    /// <param name="request">The request as it came, with its route ids: what forcing it replays.</param>
    Task CheckAsync<T>(ProfitBefore before, BlockedChangeKind kind, BlockedRequest<T> request, CancellationToken cancellationToken = default);
}

/// <param name="Profit">Null when the floor didn't apply to the project yet (no agreed amount, or internal work).</param>
public sealed record ProfitBefore(Project Project, decimal? Profit);

/// <summary>A guarded request with its route ids, kept as JSON when the floor refuses it.</summary>
/// <param name="ItemId">The material or work entry it changes, when it changes one.</param>
/// <param name="WeekStart">A labour week's first day.</param>
public sealed record BlockedRequest<T>(long ProjectId, T Request, long? ItemId = null, DateOnly? WeekStart = null);

/// <summary>Keeps a refused change once its transaction has rolled back, and alerts whoever sees the project's money.</summary>
public interface IBlockedChangeRecorder
{
    Task RecordAsync(ProfitFloorException refused, CancellationToken cancellationToken = default);
}

/// <summary>Refused changes: read, forced through once, or dismissed, by whoever sees the project's money.</summary>
public interface IBlockedChangeService
{
    Task<BlockedChangeResponse> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Replays the refused change past the floor, once.</summary>
    Task<BlockedChangeResponse> ForceAsync(long id, CancellationToken cancellationToken = default);

    Task<BlockedChangeResponse> DismissAsync(long id, CancellationToken cancellationToken = default);
}
