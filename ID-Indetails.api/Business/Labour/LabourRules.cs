using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Labour;

/// <summary>
/// Ground labour on an Execution site, reported a week at a time:
/// <list type="bullet">
/// <item>A site week runs Monday to Saturday. Sunday is never a labour day.</item>
/// <item>Weeks can be reported from the project's first week to the current one, and no day after today.</item>
/// <item>A day costs workers × daily pay, in USD. Ground labour is never billed to the client.</item>
/// <item>Defaults (crew size and day rate) are dated: a week not yet reported starts from the defaults in
/// force on each of its days. Before the first dated default, the first one applies.</item>
/// </list>
/// </summary>
public static class LabourRules
{
    public const int DaysPerWeek = 6;

    public sealed record Day(DateOnly Date, int Workers, decimal DailyPay)
    {
        public decimal Cost => Workers * DailyPay;
    }

    public sealed record Defaults(DateOnly EffectiveFrom, int Workers, decimal DailyPay);

    public static DateOnly WeekStartOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Monday to Saturday of the week starting on <paramref name="weekStart"/>.</summary>
    public static IReadOnlyList<DateOnly> WorkDays(DateOnly weekStart)
        => Enumerable.Range(0, DaysPerWeek).Select(weekStart.AddDays).ToList();

    /// <summary>The Mondays from the project's first week to the week of <paramref name="lastDay"/>, newest first.</summary>
    public static IReadOnlyList<DateOnly> ReportableWeeks(DateOnly projectStart, DateOnly lastDay)
    {
        var weeks = new List<DateOnly>();
        for (var week = WeekStartOf(lastDay); week >= WeekStartOf(projectStart); week = week.AddDays(-7))
        {
            weeks.Add(week);
        }

        return weeks;
    }

    public static RuleDenial? CheckWeek(DateOnly weekStart, DateOnly projectStart, DateOnly today)
    {
        if (weekStart.DayOfWeek != DayOfWeek.Monday)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "weekStart", "A site week starts on a Monday.");
        }

        return weekStart < WeekStartOf(projectStart) || weekStart > WeekStartOf(today)
            ? RuleDenial.BusinessRule(ErrorCodes.LabourWeekOutOfRange, "Weeks can be reported from the project's first week to the current one.")
            : null;
    }

    public static RuleDenial? CheckDays(IReadOnlyList<Day> days, DateOnly weekStart, DateOnly today)
    {
        var workDays = WorkDays(weekStart);
        if (days.Any(d => !workDays.Contains(d.Date)) || days.Select(d => d.Date).Distinct().Count() != days.Count)
        {
            return RuleDenial.Invalid(ErrorCodes.LabourDayOutsideWeek, "days", "Each day must be a different day of this week, Monday to Saturday.");
        }

        return days.Any(d => d.Date > today)
            ? RuleDenial.Invalid(ErrorCodes.LabourDayInFuture, "days", "Days after today can't be reported yet.")
            : null;
    }

    public static Defaults? DefaultsAt(IReadOnlyList<Defaults> history, DateOnly day)
    {
        var ordered = history.OrderBy(d => d.EffectiveFrom).ToList();
        return ordered.LastOrDefault(d => d.EffectiveFrom <= day) ?? ordered.FirstOrDefault();
    }

    /// <summary>An unreported week: each of its days up to today, filled in from the defaults of that day.</summary>
    public static IReadOnlyList<Day> Prefill(DateOnly weekStart, DateOnly today, IReadOnlyList<Defaults> history)
        => WorkDays(weekStart)
            .Where(day => day <= today)
            .Select(day => DefaultsAt(history, day) is { } d ? new Day(day, d.Workers, d.DailyPay) : new Day(day, 0, 0))
            .ToList();
}
