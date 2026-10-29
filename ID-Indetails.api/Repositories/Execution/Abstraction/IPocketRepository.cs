using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Execution.Abstraction;

/// <summary>A pocket movement with what the API shows about it.</summary>
public sealed record PocketEntryRow(PocketEntry Entry, string ProjectTitle, string CreatedByName);

/// <summary>A request with its site's title and the names of who made and decided it.</summary>
public sealed record PocketRequestRow(PocketRequest Request, string ProjectTitle, string? RequestedByName, string? DecidedByName);

/// <summary>
/// Movements of a kind and project, between UTC bounds (<paramref name="FromUtc"/> inclusive, <paramref name="ToUtc"/>
/// exclusive), whose project title, note or maker's name has every word of <paramref name="Text"/>; newest first.
/// </summary>
public sealed record PocketEntrySearch(
    PocketEntryKind? Kind = null,
    long? ProjectId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Text = null,
    int Page = 1,
    int PageSize = 30);

/// <summary>Execution's pocket money: its ledger and the sites' requests.</summary>
public interface IPocketRepository
{
    /// <summary>
    /// Locks the pocket until the current transaction ends (<c>SELECT … FOR UPDATE</c> on the company settings), so two
    /// payouts never spend the same balance.
    /// </summary>
    Task LockAsync(CancellationToken cancellationToken = default);

    Task<decimal> BalanceAsync(CancellationToken cancellationToken = default);

    /// <summary>Everything moved in, and everything paid out (as a positive amount).</summary>
    Task<(decimal In, decimal Out)> TotalsAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<PocketEntryRow> Items, int TotalCount)> SearchAsync(PocketEntrySearch search, CancellationToken cancellationToken = default);

    void Add(PocketEntry entry);

    /// <summary>Requests waiting for a decision, oldest first.</summary>
    Task<IReadOnlyList<PocketRequestRow>> PendingAsync(CancellationToken cancellationToken = default);

    Task<PocketRequestRow?> GetRequestAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Tracked, and locked (<c>SELECT … FOR UPDATE</c>) until the transaction ends, so it is decided once.</summary>
    Task<PocketRequest?> GetRequestForUpdateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The site's request still waiting for a decision, if any.</summary>
    Task<PocketRequest?> PendingOfAsync(long projectId, CancellationToken cancellationToken = default);

    void Add(PocketRequest request);
}
