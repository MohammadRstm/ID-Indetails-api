using ID_Indetails.api.Business.WorkingTime;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// A person's hours owed for a month (Design employees only). The month runs up to today while it is in
/// progress, from the day they joined, and up to the day they left (their account was deactivated), after which
/// their month is closed.
/// <list type="bullet">
/// <item>Expected: the required hours of every working day.</item>
/// <item>Excused hours and days (paid or not) reduce what is owed; a whole-month excuse clears it.</item>
/// <item>Overtime first makes up any shortfall, taken from the month's overtime in date order; the rest is paid as overtime.</item>
/// <item>A closed month that is still short is flagged; one in progress is "behind".</item>
/// </list>
/// </summary>
public static class HoursLedger
{
    public sealed record Excuses(bool WholeMonth, int Minutes, IReadOnlySet<DateOnly> Days);

    /// <param name="MissedDays">Working days with no work within hours and no excuse.</param>
    public sealed record Result(
        bool InProgress,
        int Expected,
        int Excused,
        int Normal,
        int Overtime,
        int MadeUp,
        int Short,
        IReadOnlyDictionary<long, int> MadeUpByWork,
        IReadOnlyList<DateOnly> MissedDays)
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
        var expected = workingDays.Sum(d => d.Hours!.Required);
        var excusedDays = workingDays.Where(d => excuses.Days.Contains(d.Day)).Sum(d => d.Hours!.Required);
        var excused = excuses.WholeMonth ? expected : Math.Min(expected, excuses.Minutes + excusedDays);

        var normal = work.Sum(w => w.Normal);
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
            missed);
    }

    public static IEnumerable<DateOnly> Days(DateOnly from, DateOnly untilExclusive)
    {
        for (var day = from; day < untilExclusive; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}
