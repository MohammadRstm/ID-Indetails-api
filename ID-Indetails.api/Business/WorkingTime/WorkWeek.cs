using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.WorkingTime;

/// <summary>
/// Working days and hours. Weekdays are numbered 0 = Sunday … 6 = Saturday; times are minutes after
/// midnight. The break is paid: it only lowers the hours required on a day longer than 6 hours.
/// </summary>
public static class WorkWeek
{
    public const int BreakAfterMinutes = 360;

    public static byte ToMask(IEnumerable<int> weekdays) => (byte)weekdays.Aggregate(0, (mask, day) => mask | (1 << day));

    public static IReadOnlyList<int> FromMask(byte mask) => Enumerable.Range(0, 7).Where(day => (mask & (1 << day)) != 0).ToList();

    public static int RequiredMinutes(int startMinute, int endMinute, int breakMinutes)
    {
        var length = endMinute - startMinute;
        return length > BreakAfterMinutes ? Math.Max(0, length - breakMinutes) : length;
    }

    /// <summary>
    /// Changes to working time (the company week or a personal schedule) start today or later, so past
    /// months never change. A change already starting today can't be replaced; a later one can.
    /// </summary>
    public static RuleDenial? CheckEffectiveFrom(DateOnly effectiveFrom, DateOnly today, bool changeExistsThatDay)
    {
        if (effectiveFrom < today)
        {
            return RuleDenial.Invalid(
                ErrorCodes.WorkingTimeInPast, "effectiveFrom", "Working-time changes start today or later; past months never change.");
        }

        return changeExistsThatDay && effectiveFrom == today
            ? RuleDenial.BusinessRule(ErrorCodes.WorkingTimeAlreadyChanged, "A change already starts today. Choose a later date.")
            : null;
    }
}
