using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Company;

/// <summary>
/// One-off company expenses: a cost paid once, charged in full on the day it was spent.
/// <list type="bullet">
/// <item>It is dated from the first of the current company month up to today.</item>
/// <item>It can be changed or removed only while its day is in the current month, so a closed month's profit never changes.</item>
/// </list>
/// </summary>
public static class OneOffExpenseRules
{
    public static RuleDenial? CheckSpentOn(DateOnly spentOn, DateOnly today)
        => spentOn < OverheadRules.MonthStart(today) || spentOn > today
            ? RuleDenial.Invalid(ErrorCodes.OneOffOutsideThisMonth, "spentOn", "A one-off expense is dated this month, up to today.")
            : null;

    /// <summary>Whether the expense's month is still the current one, so it can be changed or removed.</summary>
    public static bool IsOpen(DateOnly spentOn, DateOnly today) => spentOn >= OverheadRules.MonthStart(today);

    public static RuleDenial? CheckOpen(DateOnly spentOn, DateOnly today)
        => IsOpen(spentOn, today)
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.OneOffLocked, "This expense's month has closed, so it can't be changed.");
}
