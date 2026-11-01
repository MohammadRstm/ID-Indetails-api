using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// A working day on which someone logged less than it asked for: the next morning's reminder to them, and the list the
/// admins get. Every minute logged that day counts, overtime included. It is only a reminder: logging stays open for
/// the whole month.
/// <list type="bullet">
/// <item>Days off, days outside their employment (before joining, after leaving, in a gap) and excused days (a day
/// excuse, or the whole month) never count. An excuse of minutes names no day, so it doesn't excuse one.</item>
/// <item>From 09:00 company time the day judged is yesterday; before that it is the day before, since yesterday may
/// still be logged first thing.</item>
/// </list>
/// </summary>
public static class MissedHours
{
    public const int SendFromHour = 9;

    public sealed record Short(int LoggedMinutes, int RequiredMinutes);

    public static DateOnly DayToCheck(DateTime companyNow)
        => DateOnly.FromDateTime(companyNow).AddDays(companyNow.Hour >= SendFromHour ? -1 : -2);

    /// <returns>Null when the day asked nothing of them, or they logged enough.</returns>
    public static Short? Check(
        DateOnly day,
        PersonCalendar calendar,
        Employment employment,
        IReadOnlyList<SalaryCalculator.Excuse> excuses,
        int loggedMinutes)
    {
        var employed = day >= employment.JoinedOn && !(day > employment.LeftOn) && !Employment.InGap(employment.Gaps, day);
        var excused = excuses.Any(e => e.Kind == ExcuseKind.WholeMonth || (e.Kind == ExcuseKind.Days && e.Days.Contains(day)));
        if (!employed || excused || calendar.HoursOn(day) is not { } hours)
        {
            return null;
        }

        return loggedMinutes < hours.Required ? new Short(loggedMinutes, hours.Required) : null;
    }
}
