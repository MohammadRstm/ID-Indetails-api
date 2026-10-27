using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Pay;

public static class PayRules
{
    /// <summary>A standard month: 52 weeks ÷ 12.</summary>
    private const decimal WeeksPerMonth = 52m / 12m;

    /// <summary>
    /// What an hour of pay is worth in the month containing <paramref name="day"/>: hourly pay's own rate, or a monthly
    /// salary ÷ the paid hours (break included) the person's schedule gives that month. The salary is fixed, so a month
    /// with more working days makes each hour worth less, and a whole working day is always worth that day's share of
    /// the salary. It prices deductions and project labour; overtime uses <see cref="StandardHourlyRate"/>. 4 decimal places.
    /// </summary>
    public static decimal HourlyRate(PayBasis basis, decimal amount, DateOnly day, PersonCalendar calendar)
    {
        if (basis == PayBasis.Hourly)
        {
            return amount;
        }

        var minutes = PaidMinutesInMonth(day, calendar);
        return minutes > 0 ? Math.Round(amount * 60 / minutes, 4, MidpointRounding.AwayFromZero) : 0;
    }

    /// <summary>
    /// The true hourly rate overtime is paid at, whatever the month: hourly pay's own rate, or a monthly salary ÷ the
    /// paid hours of a standard month on the schedule in force that day (its weekly paid hours × 52 ÷ 12: 173.33 on a
    /// 40-hour week). So a month with more working days never lowers what an hour of overtime earns. 4 decimal places.
    /// </summary>
    public static decimal StandardHourlyRate(PayBasis basis, decimal amount, DateOnly day, PersonCalendar calendar)
    {
        if (basis == PayBasis.Hourly)
        {
            return amount;
        }

        var minutes = calendar.PaidMinutesPerWeek(day) * WeeksPerMonth;
        return minutes > 0 ? Math.Round(amount * 60 / minutes, 4, MidpointRounding.AwayFromZero) : 0;
    }

    /// <summary>The paid minutes, break included, of every working day in the month containing <paramref name="day"/>.</summary>
    public static int PaidMinutesInMonth(DateOnly day, PersonCalendar calendar)
    {
        var month = new DateOnly(day.Year, day.Month, 1);
        return HoursLedger.Days(month, month.AddMonths(1)).Sum(d => calendar.HoursOn(d)?.Span ?? 0);
    }

    /// <summary>Admins and super admins are paid a fixed monthly salary: they log no hours, and hours never change their pay.</summary>
    public static bool HasFixedSalary(string role) => role != RoleCodes.Employee;

    /// <summary>
    /// A fixed salary is monthly, so it can't be hourly. Execution employees are on site and log no hours, so they are
    /// paid monthly too.
    /// </summary>
    public static RuleDenial? CheckBasis(string role, string? domain, PayBasis basis)
    {
        if (basis != PayBasis.Hourly)
        {
            return null;
        }

        if (HasFixedSalary(role))
        {
            return RuleDenial.Invalid(ErrorCodes.FixedSalaryPaidMonthly, "basis", "Admins and super admins are paid a monthly salary: their pay doesn't depend on hours.");
        }

        return domain == DomainCodes.Execution
            ? RuleDenial.Invalid(ErrorCodes.FixedSalaryPaidMonthly, "basis", "Execution staff log no hours, so they are paid a monthly salary.")
            : null;
    }

    /// <summary>
    /// When a role change makes an employee an admin or super admin on <paramref name="today"/>, the day their pay becomes a
    /// fixed salary: the first of the month, since a month with any day in that role is paid as one
    /// (<see cref="RoleHistory.FixedSalaryMonth"/>). Null for any other change.
    /// </summary>
    public static DateOnly? FixedPayFrom(string previousRole, string newRole, DateOnly today)
        => !HasFixedSalary(previousRole) && HasFixedSalary(newRole) ? new DateOnly(today.Year, today.Month, 1) : null;

    /// <summary>Someone paid by the hour must be given a monthly salary before becoming an admin or super admin.</summary>
    public static RuleDenial? CheckRoleChange(string newRole, PayBasis? basisInForce)
        => HasFixedSalary(newRole) && basisInForce == PayBasis.Hourly
            ? RuleDenial.BusinessRule(ErrorCodes.FixedSalaryNeedsMonthlyPay, "Admins and super admins are paid a monthly salary: set their monthly salary before changing their role.")
            : null;
}
