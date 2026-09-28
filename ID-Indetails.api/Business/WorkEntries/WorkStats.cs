using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.WorkEntries;

/// <summary>
/// Totals and series over logged work. Every entry counts on the company-time-zone day (and week) it starts on.
/// </summary>
public static class WorkStats
{
    /// <summary>Hours a week, averaged over the last 14 days, from which a project is busy or steady.</summary>
    public const int BusyHoursPerWeek = 45;
    public const int SteadyHoursPerWeek = 15;

    /// <summary>A logged stretch, with its start in company wall-clock time.</summary>
    public sealed record Worked(long Id, long UserId, long ProjectId, DateTime LocalStart, int Minutes)
    {
        public DateOnly Day => DateOnly.FromDateTime(LocalStart);
    }

    public sealed record WeekTotal(DateOnly WeekStart, int Minutes, int Entries);

    public sealed record DayTotal(DateOnly Day, int Minutes);

    public sealed record HourCell(DayOfWeek Weekday, int Hour, int Minutes);

    public static DateOnly WeekStartOf(DateOnly day, DayOfWeek firstDay)
        => day.AddDays(-(((int)day.DayOfWeek - (int)firstDay + 7) % 7));

    public static DateOnly MonthStartOf(DateOnly day) => new(day.Year, day.Month, 1);

    public static DateOnly QuarterStartOf(DateOnly day) => new(day.Year, ((day.Month - 1) / 3 * 3) + 1, 1);

    /// <summary>Minutes of the entries starting on days from <paramref name="from"/> to <paramref name="to"/>, both inclusive.</summary>
    public static int Minutes(IEnumerable<Worked> worked, DateOnly from, DateOnly to)
        => worked.Where(w => w.Day >= from && w.Day <= to).Sum(w => w.Minutes);

    /// <summary>The <paramref name="weeks"/> weeks ending with the week of <paramref name="lastDay"/>, oldest first.</summary>
    public static IReadOnlyList<WeekTotal> Weekly(IEnumerable<Worked> worked, DateOnly lastDay, int weeks, DayOfWeek firstDay)
    {
        var first = WeekStartOf(lastDay, firstDay).AddDays(-7 * (weeks - 1));
        var byWeek = worked
            .Where(w => w.Day >= first && w.Day < first.AddDays(7 * weeks))
            .GroupBy(w => WeekStartOf(w.Day, firstDay))
            .ToDictionary(g => g.Key, g => (Minutes: g.Sum(w => w.Minutes), Entries: g.Count()));

        return Enumerable.Range(0, weeks)
            .Select(i => first.AddDays(7 * i))
            .Select(week => byWeek.TryGetValue(week, out var t) ? new WeekTotal(week, t.Minutes, t.Entries) : new WeekTotal(week, 0, 0))
            .ToList();
    }

    /// <summary>Days with any work, oldest first.</summary>
    public static IReadOnlyList<DayTotal> Daily(IEnumerable<Worked> worked)
        => worked.GroupBy(w => w.Day).OrderBy(g => g.Key).Select(g => new DayTotal(g.Key, g.Sum(w => w.Minutes))).ToList();

    /// <summary>Days with work between the two days (inclusive), and the longest run of consecutive such days.</summary>
    public static (int ActiveDays, int LongestStreak) Streaks(IEnumerable<Worked> worked, DateOnly from, DateOnly to)
    {
        var days = worked.Select(w => w.Day).Where(d => d >= from && d <= to).Distinct().Order().ToList();
        int longest = 0, run = 0;
        for (var i = 0; i < days.Count; i++)
        {
            run = i > 0 && days[i - 1].AddDays(1) == days[i] ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        return (days.Count, longest);
    }

    /// <summary>From the minutes of the last 14 days: busy from 45 h a week, steady from 15 h, quiet with any work.</summary>
    public static ActivityLevel LevelOf(int minutesLast14Days)
    {
        var hoursPerWeek = minutesLast14Days / 60m / 2;
        return hoursPerWeek >= BusyHoursPerWeek ? ActivityLevel.Busy
            : hoursPerWeek >= SteadyHoursPerWeek ? ActivityLevel.Steady
            : minutesLast14Days > 0 ? ActivityLevel.Quiet
            : ActivityLevel.None;
    }

    /// <summary>When work happens: minutes per weekday and hour of the day, each entry spread over the hours it covers.</summary>
    public static IReadOnlyList<HourCell> HourHeat(IEnumerable<Worked> worked)
    {
        var cells = new Dictionary<(DayOfWeek, int), int>();
        foreach (var w in worked)
        {
            var at = w.LocalStart;
            var left = w.Minutes;
            while (left > 0)
            {
                var inThisHour = Math.Min(left, 60 - at.Minute);
                var key = (at.DayOfWeek, at.Hour);
                cells[key] = cells.GetValueOrDefault(key) + inThisHour;
                at = at.AddMinutes(inThisHour);
                left -= inThisHour;
            }
        }

        return cells
            .OrderBy(c => c.Key.Item1)
            .ThenBy(c => c.Key.Item2)
            .Select(c => new HourCell(c.Key.Item1, c.Key.Item2, c.Value))
            .ToList();
    }
}
