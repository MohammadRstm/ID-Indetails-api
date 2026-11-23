using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.WorkEntries.Abstraction;

public sealed record ReopenedMonthView(DateOnly Month, DateOnly OpenUntil, string Note, long ReopenedById, string ReopenedByName, DateTime ReopenedAt);

public interface IReopenedMonthRepository
{
    /// <summary>The reopened months still open on <paramref name="today"/>, oldest first. Not tracked.</summary>
    Task<IReadOnlyList<ReopenedMonthView>> OpenAsync(DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Tracked.</summary>
    Task<ReopenedMonth?> GetAsync(DateOnly month, CancellationToken cancellationToken = default);

    void Add(ReopenedMonth month);

    void Remove(ReopenedMonth month);
}
