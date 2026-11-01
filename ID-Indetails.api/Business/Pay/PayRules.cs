using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Pay;

public static class PayRules
{
    /// <summary>The hours a month the client counts for a salary's overtime rate.</summary>
    public const decimal OvertimeHoursPerMonth = 184m;

    /// <summary>
    /// What an hour of pay is worth in the month containing <paramref name="day"/>: hourly pay's own rate, or a monthly
    /// salary ÷ the paid hours (break included) the person's schedule gives that month. The salary is fixed, so a month
    /// with more working days makes each hour worth less, and a whole working day is always worth that day's share of
    /// the salary. It prices deductions and project labour; overtime is paid at the pay's own overtime rate
    /// (<see cref="SuggestedOvertimeRate"/>). 4 decimal places.
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
    /// The overtime rate a pay change is given when none is typed: hourly pay's own rate, or a salary ÷ 184, to the cent,
    /// for everyone whatever their schedule. It is stored with the pay and used all the while that pay is in force;
    /// admins may type another for a salary. Overtime is paid at it × the company's multiplier.
    /// </summary>
    public static decimal SuggestedOvertimeRate(PayBasis basis, decimal amount)
        => basis == PayBasis.Hourly ? amount : Math.Round(amount / OvertimeHoursPerMonth, 2, MidpointRounding.AwayFromZero);

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
