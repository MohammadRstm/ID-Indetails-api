using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Payroll;

/// <summary>
/// Excusing hours in someone's month: some minutes, specific days of that month, or the whole month.
/// Paid by default; an unpaid excuse is deducted from salaried pay. Never for a future month.
/// </summary>
public static class ExcuseRules
{
    public static RuleDenial? Check(DateOnly month, DateOnly today, ExcuseKind kind, IReadOnlyList<DateOnly> days)
    {
        if (month > new DateOnly(today.Year, today.Month, 1))
        {
            return RuleDenial.Invalid(ErrorCodes.ExcuseMonthInFuture, "month", "Hours can't be excused for a future month.");
        }

        return kind == ExcuseKind.Days && days.Any(d => d.Year != month.Year || d.Month != month.Month)
            ? RuleDenial.Invalid(ErrorCodes.ValidationFailed, "days", "Every excused day must be in that month.")
            : null;
    }

    public static string DefaultNote(ExcuseKind kind, bool isPaid) => (kind, isPaid) switch
    {
        (ExcuseKind.WholeMonth, true) => "Whole month excused",
        (ExcuseKind.WholeMonth, false) => "Whole month unpaid",
        (ExcuseKind.Days, true) => "Day off",
        (ExcuseKind.Days, false) => "Unpaid day off",
        (_, true) => "Excused",
        _ => "Unpaid leave",
    };
}
