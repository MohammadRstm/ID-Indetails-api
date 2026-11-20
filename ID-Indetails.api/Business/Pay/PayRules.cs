using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Pay;

public static class PayRules
{
    /// <summary>
    /// The hours a month a salary's overtime rate is worked out from when the working time sets no overtime threshold
    /// (with one, its hours are used: 184 from 1 November 2026).
    /// </summary>
    public const decimal OvertimeHoursPerMonth = 184m;

    /// <summary>
    /// What an hour of a monthly salary is worth in the month containing <paramref name="day"/>: the salary ÷ the paid
    /// hours (break included) the person's schedule gives that month. The salary is fixed, so a month
    /// with more working days makes each hour worth less, and a whole working day is always worth that day's share of
    /// the salary. It prices deductions and project labour; overtime is paid at the pay's own overtime rate
    /// (<see cref="SuggestedOvertimeRate"/>). 4 decimal places.
    /// </summary>
    public static decimal HourlyRate(decimal salary, DateOnly day, PersonCalendar calendar)
    {
        var minutes = PaidMinutesInMonth(day, calendar);
        return minutes > 0 ? Math.Round(salary * 60 / minutes, 4, MidpointRounding.AwayFromZero) : 0;
    }

    /// <summary>
    /// The overtime rate a pay change is given when none is typed: the salary ÷ the working time's overtime threshold
    /// (184 hours; 184 too when it sets none), to the cent, for everyone whatever their schedule. It is stored with the
    /// pay and used all the while that pay is in force; admins may type another.
    /// Overtime is paid at it × the company's multiplier.
    /// </summary>
    /// <param name="thresholdHours">The working time's overtime threshold in force; 0 when it sets none.</param>
    public static decimal SuggestedOvertimeRate(decimal salary, int thresholdHours = 0)
        => Math.Round(salary / (thresholdHours > 0 ? thresholdHours : OvertimeHoursPerMonth), 2, MidpointRounding.AwayFromZero);

    /// <summary>The paid minutes, break included, of every working day in the month containing <paramref name="day"/>.</summary>
    public static int PaidMinutesInMonth(DateOnly day, PersonCalendar calendar)
    {
        var month = new DateOnly(day.Year, day.Month, 1);
        return HoursLedger.Days(month, month.AddMonths(1)).Sum(d => calendar.HoursOn(d)?.Span ?? 0);
    }

    /// <summary>Admins and super admins are paid a fixed monthly salary: they log no hours, and hours never change their pay.</summary>
    public static bool HasFixedSalary(string role) => role != RoleCodes.Employee;

    /// <summary>
    /// When a role change makes an employee an admin or super admin on <paramref name="today"/>, the day their pay becomes a
    /// fixed salary: the first of the month, since a month with any day in that role is paid as one
    /// (<see cref="RoleHistory.FixedSalaryMonth"/>). Null for any other change.
    /// </summary>
    public static DateOnly? FixedPayFrom(string previousRole, string newRole, DateOnly today)
        => !HasFixedSalary(previousRole) && HasFixedSalary(newRole) ? new DateOnly(today.Year, today.Month, 1) : null;
}
