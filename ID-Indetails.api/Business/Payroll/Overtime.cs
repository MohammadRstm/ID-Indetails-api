using ID_Indetails.api.Business.WorkingTime;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// Splits logged work into normal and overtime minutes, day by day. Overtime is:
/// <list type="bullet">
/// <item>all time on a day off, and all of an entry approved as overtime on a day of time off
/// (<see cref="Work.AllOvertime"/>), which also leaves the day's hours to the rest of its work;</item>
/// <item>time before the day's start or after its end;</item>
/// <item>time within the hours beyond the day's required hours, taken from the latest work of the day. From
/// <see cref="WorkWeek.BreakCountsFrom"/> that time is the break worked, not overtime: the whole span is normal.</item>
/// </list>
/// </summary>
public static class Overtime
{
    /// <param name="Start">Minutes after midnight (company time) the work started.</param>
    /// <param name="AllOvertime">Hours on a day of time off approved as overtime: every minute is overtime.</param>
    public sealed record Work(long Id, DateOnly Day, int Start, int Minutes, bool AllOvertime = false)
    {
        public int End => Math.Min(24 * 60, Start + Minutes);
    }

    public sealed record Split(long Id, DateOnly Day, int Minutes, int Overtime)
    {
        public int Normal => Minutes - Overtime;
    }

    public static IReadOnlyList<Split> Of(IEnumerable<Work> work, PersonCalendar calendar)
    {
        var splits = new List<Split>();

        foreach (var day in work.GroupBy(w => w.Day))
        {
            var hours = calendar.HoursOn(day.Key);
            splits.AddRange(day.Where(w => hours is null || w.AllOvertime).Select(w => new Split(w.Id, w.Day, w.Minutes, w.Minutes)));
            if (hours is null)
            {
                continue;
            }

            var counted = day.Where(w => !w.AllOvertime).ToList();
            var inside = counted.ToDictionary(w => w.Id, w => Math.Max(0, Math.Min(w.End, hours.End) - Math.Max(w.Start, hours.Start)));
            var excess = Math.Max(0, inside.Values.Sum() - hours.InHours);

            foreach (var w in counted.OrderByDescending(w => w.End).ThenByDescending(w => w.Start))
            {
                var fromExcess = Math.Min(excess, inside[w.Id]);
                excess -= fromExcess;
                splits.Add(new Split(w.Id, w.Day, w.Minutes, w.Minutes - inside[w.Id] + fromExcess));
            }
        }

        return splits.OrderBy(s => s.Day).ThenBy(s => s.Id).ToList();
    }
}
