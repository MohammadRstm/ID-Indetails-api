using ID_Indetails.api.Business.WorkingTime;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// Splits logged work into normal and overtime minutes, day by day. Overtime is:
/// <list type="bullet">
/// <item>all time on a day off;</item>
/// <item>time before the day's start or after its end;</item>
/// <item>time within the hours beyond the day's required hours, taken from the latest work of the day.</item>
/// </list>
/// </summary>
public static class Overtime
{
    /// <param name="Start">Minutes after midnight (company time) the work started.</param>
    public sealed record Work(long Id, DateOnly Day, int Start, int Minutes)
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
            if (hours is null)
            {
                splits.AddRange(day.Select(w => new Split(w.Id, w.Day, w.Minutes, w.Minutes)));
                continue;
            }

            var inside = day.ToDictionary(w => w.Id, w => Math.Max(0, Math.Min(w.End, hours.End) - Math.Max(w.Start, hours.Start)));
            var excess = Math.Max(0, inside.Values.Sum() - hours.Required);

            foreach (var w in day.OrderByDescending(w => w.End).ThenByDescending(w => w.Start))
            {
                var fromExcess = Math.Min(excess, inside[w.Id]);
                excess -= fromExcess;
                splits.Add(new Split(w.Id, w.Day, w.Minutes, w.Minutes - inside[w.Id] + fromExcess));
            }
        }

        return splits.OrderBy(s => s.Day).ThenBy(s => s.Id).ToList();
    }
}
