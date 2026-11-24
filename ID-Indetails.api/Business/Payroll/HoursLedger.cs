using ID_Indetails.api.Business.WorkingTime;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// A person's hours owed for a month (Design employees only). The month runs up to today while it is in
/// progress, from the day they joined, and up to the day they left (their account was deactivated), after which
/// their month is closed.
/// <list type="bullet">
/// <item>Expected: the required hours of every working day; from <see cref="WorkWeek.BreakCountsFrom"/> its whole span,
/// the break included. Then the break taken that day counts as worked (<see cref="Result.Breaks"/>), and the rest of
/// the break of a day short of its hours, paid by the company, counts as excused (<see cref="Result.BreaksCovered"/>).</item>
/// <item>Excused hours and days (paid or not) reduce what is owed; a whole-month excuse clears it.</item>
/// <item>Overtime first makes up any shortfall, taken from the month's overtime in date order; the rest is paid as overtime.</item>
/// <item>A closed month that is still short is flagged; one in progress is "behind".</item>
/// </list>
/// </summary>
public static class HoursLedger
{
    public sealed record Excuses(bool WholeMonth, int Minutes, IReadOnlySet<DateOnly> Days);

    /// <param name="Normal">Work within hours, plus the break taken once the break counts.</param>
    /// <param name="Excused">Excused minutes, plus the break no project carries once the break counts.</param>
    /// <param name="MissedDays">Working days with no work within hours and no excuse.</param>
    /// <param name="Required">The working days' required minutes, whatever counts: what excuses are measured against.</param>
    /// <param name="Breaks">The break taken, counted in <paramref name="Normal"/> (0 before the break counts).</param>
    /// <param name="BreaksCovered">The break the company paid on days short of their hours, counted in <paramref name="Excused"/>.</param>
    public sealed record Result(
        bool InProgress,
        int Expected,
        int Excused,
        int Normal,
        int Overtime,
        int MadeUp,
        int Short,
        IReadOnlyDictionary<long, int> MadeUpByWork,
        IReadOnlyList<DateOnly> MissedDays,
        int Required = 0,
        int Breaks = 0,
        int BreaksCovered = 0)
    {
        public int OvertimePaid => Overtime - MadeUp;

        public bool Flagged => !InProgress && Short > 0;

        public bool Behind => InProgress && Short > 0;
    }

    public static Result Build(
        DateOnly month,
        DateOnly today,
        DateOnly joinedOn,
        PersonCalendar calendar,
        IReadOnlyList<Overtime.Split> work,
        Excuses excuses,
        DateOnly? leftOn = null,
        IReadOnlyList<Employment.Gap>? gaps = null)
    {
        var next = month.AddMonths(1);
        var inProgress = today >= month && today < next && !(today > leftOn);
        var until = inProgress ? today : next;
        if (leftOn is { } left && left.AddDays(1) < until)
        {
            until = left.AddDays(1);
        }

        var from = joinedOn > month ? joinedOn : month;

        // Days away between two periods of employment aren't expected.
        var workingDays = Days(from, until)
            .Where(d => !Employment.InGap(gaps, d))
            .Select(d => (Day: d, Hours: calendar.HoursOn(d)))
            .Where(d => d.Hours is not null)
            .ToList();
        var expected = workingDays.Sum(d => d.Hours!.Expected);
        var excusedDays = workingDays.Where(d => excuses.Days.Contains(d.Day)).Sum(d => d.Hours!.Expected);

        // once the break counts: the break taken is worked; the rest of a short day's break is the company's, so covered
        var normalByDay = work.GroupBy(w => w.Day).ToDictionary(g => g.Key, g => g.Sum(w => w.Normal));
        var counted = workingDays.Where(d => d.Hours!.BreakCounts && !excuses.Days.Contains(d.Day))
            .Select(d => (d.Hours!, Normal: normalByDay.GetValueOrDefault(d.Day)))
            .ToList();
        var breaks = counted.Sum(d => d.Item1.BreakTaken(d.Normal));
        var covered = counted.Sum(d => d.Item1.BreakUnshared(d.Normal));
        var excused = excuses.WholeMonth ? expected : Math.Min(expected, excuses.Minutes + excusedDays + covered);

        var normal = work.Sum(w => w.Normal) + breaks;
        var overtime = work.Sum(w => w.Overtime);
        var madeUp = Math.Min(overtime, Math.Max(0, expected - excused - normal));

        var madeUpByWork = new Dictionary<long, int>();
        var toAllocate = madeUp;
        foreach (var w in work.Where(w => w.Overtime > 0))
        {
            if (toAllocate == 0)
            {
                break;
            }

            var take = Math.Min(toAllocate, w.Overtime);
            madeUpByWork[w.Id] = take;
            toAllocate -= take;
        }

        var workedDays = work.Where(w => w.Normal > 0).Select(w => w.Day).ToHashSet();
        var missed = excuses.WholeMonth
            ? []
            : workingDays.Where(d => !workedDays.Contains(d.Day) && !excuses.Days.Contains(d.Day)).Select(d => d.Day).ToList();

        return new Result(
            inProgress,
            expected,
            excused,
            normal,
            overtime,
            madeUp,
            Math.Max(0, expected - excused - normal) - madeUp,
            madeUpByWork,
            missed,
            workingDays.Sum(d => d.Hours!.Required),
            breaks,
            covered);
    }

    public static IEnumerable<DateOnly> Days(DateOnly from, DateOnly untilExclusive)
    {
        for (var day = from; day < untilExclusive; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}
