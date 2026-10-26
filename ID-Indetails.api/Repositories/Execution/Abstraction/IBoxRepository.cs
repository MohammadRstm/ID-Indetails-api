using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Execution.Abstraction;

/// <summary>A box movement with what the API shows about it.</summary>
public sealed record BoxEntryRow(BoxEntry Entry, string ProjectTitle, string? CreatedByName);

/// <summary>An Execution project as the Box page lists it, with what it put into the box and took out of it.</summary>
public sealed record BoxProjectRow(Project Project, string? ClientName, decimal Contributed, decimal FromBox);

/// <summary>The Execution box: its ledger, and where each Execution project stands with it.</summary>
public interface IBoxRepository
{
    /// <summary>
    /// Locks the box until the current transaction ends (<c>SELECT … FOR UPDATE</c> on the company settings), so two
    /// movements out never both spend the same balance.
    /// </summary>
    Task LockAsync(CancellationToken cancellationToken = default);

    Task<decimal> BalanceAsync(CancellationToken cancellationToken = default);

    /// <summary>What the project has put into the box: every entry of it but the money moved to it.</summary>
    Task<decimal> ContributedAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>The latest time the project's revenue was taken out, or null.</summary>
    Task<BoxEntry?> LastRemovalAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Whether the payment's money is in the box: it went in and hasn't been given back.</summary>
    Task<bool> HoldsPaymentAsync(long paymentId, CancellationToken cancellationToken = default);

    /// <summary>Execution projects that aren't archived, or still stand in the box, with their client and money.</summary>
    Task<IReadOnlyList<BoxProjectRow>> ProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>Movements, newest first.</summary>
    Task<(IReadOnlyList<BoxEntryRow> Items, int TotalCount)> SearchAsync(long? projectId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<BoxEntryRow?> GetAsync(long id, CancellationToken cancellationToken = default);

    void Add(BoxEntry entry);
}
