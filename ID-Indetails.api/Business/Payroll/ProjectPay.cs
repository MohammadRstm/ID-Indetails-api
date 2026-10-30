using ID_Indetails.api.Business.WorkingTime;

namespace ID_Indetails.api.Business.Payroll;

public enum ProjectPayUnit
{
    Week,
    Month,
    Quarter,
}

/// <summary>
/// What a Design project paid its people. Each work entry is paid as Salaries pays it: its hours within schedule,
/// plus any overtime that made up a shortfall, at the person's hourly rate in force that day (a monthly salary's rate
/// in that month, see <see cref="Pay.PayRules.HourlyRate"/>), and the rest of its overtime at the pay's overtime rate
/// × the multiplier. Paid breaks and salary deductions are left out:
/// they aren't worked on any project.
/// </summary>
public static class ProjectPay
{
    /// <param name="HourlyRate">The rate of its hours within schedule.</param>
    /// <param name="Priced">False when the person had no pay record that day (paid 0).</param>
    public sealed record EntryPay(int Normal, int Overtime, decimal HourlyRate, string Currency, decimal Pay, bool Priced);

    public static EntryPay Of(Overtime.Split work, int madeUp, SalaryCalculator.PayTerms? terms, PersonCalendar calendar)
    {
        var normal = work.Normal + madeUp;
        var overtime = work.Overtime - madeUp;
        if (terms is null)
        {
            return new EntryPay(normal, overtime, 0, "USD", 0, false);
        }

        var rate = terms.HourlyRateOn(work.Day, calendar);
        var overtimeRate = terms.OvertimeRate * calendar.PolicyOn(work.Day).OvertimeMultiplier;
        var pay = normal / 60m * rate + overtime / 60m * overtimeRate;
        return new EntryPay(normal, overtime, rate, terms.Currency, pay, true);
    }

    /// <summary>The week, month or quarter containing <paramref name="day"/>: first day, and the first day after it.</summary>
    public static (DateOnly From, DateOnly Until) Period(ProjectPayUnit unit, DateOnly day, DayOfWeek weekStart)
    {
        switch (unit)
        {
            case ProjectPayUnit.Week:
                var back = ((int)day.DayOfWeek - (int)weekStart + 7) % 7;
                var start = day.AddDays(-back);
                return (start, start.AddDays(7));
            case ProjectPayUnit.Quarter:
                var quarter = new DateOnly(day.Year, (day.Month - 1) / 3 * 3 + 1, 1);
                return (quarter, quarter.AddMonths(3));
            default:
                var month = new DateOnly(day.Year, day.Month, 1);
                return (month, month.AddMonths(1));
        }
    }
}
