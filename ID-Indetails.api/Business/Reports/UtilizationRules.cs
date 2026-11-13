namespace ID_Indetails.api.Business.Reports;

/// <summary>
/// How much of their expected hours Design employees logged in a month: hours only, never pay. Expected hours and
/// excuses come from the month's hours ledger; overtime is the time past each day's required hours, as employees see
/// it. Which overtime made up missing hours, the shortfall and the overtime paid are pay information and stay out.
/// </summary>
public static class UtilizationRules
{
    /// <param name="Logged">Every minute logged in the month (to today while it runs).</param>
    /// <param name="Expected">The required minutes of the month's working days.</param>
    /// <param name="Excused">Excused minutes and days, paid or not, within <paramref name="Expected"/>.</param>
    /// <param name="Overtime">Minutes past each day's required hours.</param>
    public sealed record Hours(int Logged, int Expected, int Excused, int Overtime);

    /// <param name="Available">Expected less excused: what there was to log.</param>
    /// <param name="Utilization">Logged ÷ available, a fraction (0.92 is 92%; past 1 with overtime); null with nothing available.</param>
    public sealed record Totals(int Logged, int Expected, int Excused, int Available, int Overtime, decimal? Utilization);

    public static Totals Of(Hours hours) => Of([hours]);

    /// <summary>The sum of several people's hours, and the utilization of that sum.</summary>
    public static Totals Of(IEnumerable<Hours> hours)
    {
        var list = hours.ToList();
        var logged = list.Sum(h => h.Logged);
        var expected = list.Sum(h => h.Expected);
        var excused = list.Sum(h => h.Excused);
        var available = Math.Max(0, expected - excused);
        return new Totals(
            logged,
            expected,
            excused,
            available,
            list.Sum(h => h.Overtime),
            available > 0 ? Math.Round((decimal)logged / available, 4, MidpointRounding.AwayFromZero) : null);
    }

    /// <summary>Each project's minutes and its share of the person's logged minutes, the most first.</summary>
    public static IReadOnlyList<(long ProjectId, int Minutes, decimal Share)> Mix(IEnumerable<(long ProjectId, int Minutes)> work)
    {
        var byProject = work.GroupBy(w => w.ProjectId).Select(g => (ProjectId: g.Key, Minutes: g.Sum(w => w.Minutes))).Where(p => p.Minutes > 0).ToList();
        var total = byProject.Sum(p => p.Minutes);
        return byProject
            .OrderByDescending(p => p.Minutes)
            .ThenBy(p => p.ProjectId)
            .Select(p => (p.ProjectId, p.Minutes, Math.Round((decimal)p.Minutes / total, 4, MidpointRounding.AwayFromZero)))
            .ToList();
    }
}
