using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Company;

/// <summary>
/// Company overhead: recurring monthly costs that belong to neither domain.
/// <list type="bullet">
/// <item>A line is charged for every month from its start month to its end month, in full.</item>
/// <item>The amount charged is the latest amount dated on or before that month. Amount changes apply
/// from the first of a month, never an earlier month than the current one, so past months keep their cost.</item>
/// <item>Cancelling ends a line at the end of the current month. It can be resumed until that date
/// passes; after that, a new line is added.</item>
/// <item>Totals are kept per currency and never converted.</item>
/// </list>
/// </summary>
public static class OverheadRules
{
    public sealed record Amount(DateOnly EffectiveFrom, decimal Value);

    public static DateOnly MonthStart(DateOnly day) => new(day.Year, day.Month, 1);

    public static DateOnly MonthEnd(DateOnly day) => MonthStart(day).AddMonths(1).AddDays(-1);

    /// <returns>What the line costs in the month starting <paramref name="month"/>, or null when it isn't charged.</returns>
    public static decimal? AmountFor(DateOnly month, DateOnly startedOn, DateOnly? endedOn, IEnumerable<Amount> amounts)
    {
        if (startedOn > MonthEnd(month) || (endedOn is { } end && end < month))
        {
            return null;
        }

        return amounts.Where(a => a.EffectiveFrom <= month).OrderBy(a => a.EffectiveFrom).LastOrDefault()?.Value;
    }

    public static RuleDenial? CheckMonth(DateOnly month, DateOnly today, DateOnly? latestAmountFrom, string field)
    {
        if (month < MonthStart(today))
        {
            return RuleDenial.Invalid(ErrorCodes.OverheadMonthInPast, field, "Past months keep their cost; choose this month or a later one.");
        }

        return latestAmountFrom is { } latest && month < latest
            ? RuleDenial.BusinessRule(
                ErrorCodes.OverheadMonthInPast, "A later amount is already set. Change it from that month or later.")
            : null;
    }

    public static bool HasEnded(DateOnly? endedOn, DateOnly today) => endedOn is { } end && end < today;
}
