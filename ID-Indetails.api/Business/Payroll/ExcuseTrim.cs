using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// Hours logged where an excuse stood in for them (23 November 2026): the excuse gives way, so nothing counts twice.
/// <list type="bullet">
/// <item>A day excuse loses the days the new work falls on, once those days have work within hours.</item>
/// <item>Minute excuses lose only what the month's work within hours now makes redundant: what the work within hours
/// and the excuses add up to past the whole month's required hours, oldest excuse first, paid or not. Overtime never
/// counts against an excuse, and an excuse planned for later in the month stays while the month still needs it.</item>
/// <item>A whole-month excuse is never changed: whoever gave it is told and decides.</item>
/// </list>
/// </summary>
public static class ExcuseTrim
{
    public sealed record Excuse(long Id, ExcuseKind Kind, int? Minutes, IReadOnlyList<DateOnly> Days, DateTime CreatedAt);

    /// <summary>What happens to an excuse: it goes, or is replaced by one with these days or minutes.</summary>
    public sealed record Change(long ExcuseId, bool Removed, IReadOnlyList<DateOnly> Days, int? Minutes, IReadOnlyList<DateOnly> DaysTaken, int MinutesTaken);

    /// <param name="workedDays">The days the new work falls on that now have work within hours.</param>
    /// <param name="normalMinutes">The month's work within hours, the new work included.</param>
    /// <param name="expectedMinutes">The whole month's required minutes (every working day of their employment).</param>
    /// <param name="requiredOn">A day's required minutes (0 off a working day).</param>
    public static IReadOnlyList<Change> Of(
        IReadOnlyList<Excuse> excuses,
        IReadOnlySet<DateOnly> workedDays,
        int normalMinutes,
        int expectedMinutes,
        Func<DateOnly, int> requiredOn)
    {
        if (excuses.Any(e => e.Kind == ExcuseKind.WholeMonth))
        {
            return [];
        }

        var changes = new List<Change>();
        var dayExcused = 0;
        foreach (var e in excuses.Where(e => e.Kind == ExcuseKind.Days))
        {
            var taken = e.Days.Where(workedDays.Contains).ToList();
            var left = e.Days.Except(taken).ToList();
            dayExcused += left.Sum(requiredOn);
            if (taken.Count > 0)
            {
                changes.Add(new Change(e.Id, left.Count == 0, left, null, taken, 0));
            }
        }

        var minuteExcuses = excuses.Where(e => e.Kind == ExcuseKind.Minutes).OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToList();
        var redundant = normalMinutes + dayExcused + minuteExcuses.Sum(e => e.Minutes ?? 0) - expectedMinutes;
        foreach (var e in minuteExcuses)
        {
            if (redundant <= 0)
            {
                break;
            }

            var minutes = e.Minutes ?? 0;
            var take = Math.Min(minutes, redundant);
            redundant -= take;
            changes.Add(new Change(e.Id, take == minutes, [], minutes - take, [], take));
        }

        return changes;
    }
}
