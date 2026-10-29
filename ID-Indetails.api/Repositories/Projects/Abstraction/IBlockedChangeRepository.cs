using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Projects.Abstraction;

/// <summary>A refused change with its project and the names of who made and decided it.</summary>
public sealed record BlockedChangeRow(BlockedChange Change, Project Project, string RequestedByName, string? DecidedByName);

/// <summary>Changes the profit floor refused (see <c>Business/Projects/ProfitFloorRules</c>).</summary>
public interface IBlockedChangeRepository
{
    Task<BlockedChangeRow?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decides a pending change at once, outside any transaction (forcing replays a service that opens its own): false
    /// when it was decided already, so it is forced or dismissed once.
    /// </summary>
    Task<bool> DecideAsync(long id, BlockedChangeStatus status, long deciderId, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>Makes a change pending again, when forcing it failed.</summary>
    Task ReopenAsync(long id, CancellationToken cancellationToken = default);

    Task SetResultAsync(long id, long resultId, CancellationToken cancellationToken = default);

    void Add(BlockedChange change);
}
