using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Employees;

/// <summary>Leaving and being taken back, as dated periods of employment (see <see cref="Payroll.Employment"/>).</summary>
public static class EmploymentRules
{
    /// <summary>
    /// The last working day given when someone leaves: not after today, not before their period started, and not so
    /// far back that a closed month would change, since every day after it stops being paid.
    /// </summary>
    public static RuleDenial? CheckLastWorkingDay(DateOnly lastDay, DateOnly startedOn, DateOnly today)
    {
        if (lastDay > today)
        {
            return RuleDenial.Invalid(ErrorCodes.LastWorkingDayInvalid, "lastWorkingDay", "The last working day can't be in the future.");
        }

        if (lastDay < startedOn)
        {
            return RuleDenial.Invalid(
                ErrorCodes.LastWorkingDayInvalid, "lastWorkingDay", $"The last working day can't be before they started, on {startedOn:yyyy-MM-dd}.");
        }

        return lastDay.AddDays(1) < WorkEntryRules.OpenFrom(today)
            ? RuleDenial.BusinessRule(ErrorCodes.MonthClosed, "Past months never change: the last working day must leave closed months as they are.")
            : null;
    }

    /// <summary>
    /// Whether taking someone back asks if their time away is paid: they left earlier this month, so the gap falls in
    /// a month still being paid. Time away that started in an earlier month is never paid.
    /// </summary>
    public static bool AsksAboutGap(DateOnly leftOn, DateOnly today)
        => leftOn.Year == today.Year && leftOn.Month == today.Month && GapDays(leftOn, today).Any();

    /// <summary>The days they were away: after their last working day, before today.</summary>
    public static IEnumerable<DateOnly> GapDays(DateOnly leftOn, DateOnly today)
    {
        for (var day = leftOn.AddDays(1); day < today; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}
