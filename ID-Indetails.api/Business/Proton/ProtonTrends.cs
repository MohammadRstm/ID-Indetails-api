namespace ID_Indetails.api.Business.Proton;

/// <param name="Pct">The difference as a whole percentage of <paramref name="Previous"/>; null when there was nothing before.</param>
public sealed record ProtonChange(decimal Current, decimal Previous, decimal Diff, int? Pct);

/// <param name="SharePct">The part of the total difference this one makes up; null when the total didn't change.</param>
public sealed record ProtonContributor<TKey>(TKey Key, ProtonChange Change, int? SharePct);

/// <summary>
/// The arithmetic behind Proton's comparisons, done here rather than by the model: differences, percentages, shares
/// and what each part contributed to a change. Inputs are figures the existing services calculated.
/// </summary>
public static class ProtonTrends
{
    public static ProtonChange Change(decimal current, decimal previous)
    {
        var diff = current - previous;
        return new ProtonChange(current, previous, diff, previous == 0 ? null : Pct(diff, Math.Abs(previous)));
    }

    /// <summary>A whole percentage of <paramref name="total"/>; 0 when the total is 0.</summary>
    public static int SharePct(decimal part, decimal total) => total == 0 ? 0 : Pct(part, total);

    /// <summary>
    /// Each part's change and its share of the total change, the biggest movers first (ties by key). Shares can be
    /// negative or pass 100 when parts moved in opposite directions; they always add up to 100.
    /// </summary>
    public static IReadOnlyList<ProtonContributor<TKey>> Contributors<TKey>(IEnumerable<(TKey Key, decimal Current, decimal Previous)> parts)
    {
        var changes = parts.Select(p => (p.Key, Change: Change(p.Current, p.Previous))).ToList();
        var total = changes.Sum(c => c.Change.Diff);
        return changes
            .Where(c => c.Change.Diff != 0)
            .OrderByDescending(c => Math.Abs(c.Change.Diff))
            .ThenBy(c => c.Key)
            .Select(c => new ProtonContributor<TKey>(c.Key, c.Change, total == 0 ? null : Pct(c.Change.Diff, total)))
            .ToList();
    }

    /// <summary>
    /// The average of the last <paramref name="periods"/> earlier periods, as a baseline to judge the current one by;
    /// null unless there are that many and each of them has something in it.
    /// </summary>
    public static decimal? Baseline(IReadOnlyList<decimal> earlier, int periods)
    {
        if (earlier.Count < periods)
        {
            return null;
        }

        var last = earlier.Skip(earlier.Count - periods).ToList();
        return last.All(v => v > 0) ? last.Average() : null;
    }

    private static int Pct(decimal part, decimal total) => (int)Math.Round(part / total * 100, MidpointRounding.AwayFromZero);
}
