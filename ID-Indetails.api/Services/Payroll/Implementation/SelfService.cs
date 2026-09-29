using System.Globalization;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Payroll.Implementation;

/// <summary>What the pay, hours and overtime views share: the caller's own, and someone's for people who may see them.</summary>
internal static class SelfService
{
    /// <summary>The month asked for (this month by default): never in the future or before the person joined.</summary>
    public static DateOnly MonthOf(string? text, DateOnly today, DateOnly joinedOn)
    {
        var month = text is null
            ? new DateOnly(today.Year, today.Month, 1)
            : DateOnly.ParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (month > today)
        {
            throw new RequestValidationException(ErrorCodes.SalaryMonthInFuture, "month", "A future month can't be shown.");
        }

        return month < new DateOnly(joinedOn.Year, joinedOn.Month, 1)
            ? throw new RequestValidationException(ErrorCodes.ValidationFailed, "month", "That month is before they joined.")
            : month;
    }

    public static MyLedgerMonthResponse LedgerMonth(PersonMonth personMonth, HoursLedger.Result ledger)
    {
        var input = personMonth.Input;
        var wholeMonth = input.Excuses.Any(e => e.Kind == ExcuseKind.WholeMonth);

        return new MyLedgerMonthResponse(
            input.Month,
            ledger.InProgress,
            MyTime.StatusOf(ledger, wholeMonth),
            ledger.Expected,
            MyTime.ExpectedFullMonth(input.Month, input.JoinedOn, input.Calendar),
            ledger.Excused,
            wholeMonth,
            ledger.Normal,
            ledger.MadeUp,
            ledger.OvertimePaid,
            ledger.Short,
            ledger.MissedDays,
            personMonth.Excuses.Select(Excuse).ToList());
    }

    /// <summary>An excuse as the caller sees it: who made it isn't shown.</summary>
    public static MyExcuseResponse Excuse(HourExcuse x)
        => new(x.Kind, x.Minutes, x.Days.Select(d => d.Day).OrderBy(d => d).ToList(), x.IsPaid, x.Note);
}
