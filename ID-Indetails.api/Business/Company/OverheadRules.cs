using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Company;

/// <summary>
/// Company overhead: recurring monthly costs that belong to neither domain.
/// <list type="bullet">
/// <item>A line is charged for every month from its start month to its end month, in full.</item>
/// <item>The amount charged is the latest amount dated on or before that month. Amount changes apply
/// from the first of a month, never an earlier month than the current one, so past months keep their cost.</item>
/// <item>Cancelling ends a line at the end of the current month. It can be resumed until that date
/// passes; after that, a new line is added.</item>
/// <item>A line's currency changes from the first of a month, this month or later. A line already charged
/// before that month ends and a new one with the same name and group takes over, so past months keep their
/// currency; one not charged before it just changes.</item>
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

    public static RuleDenial? CheckCurrencyChange(string currency, string newCurrency, DateOnly? endedOn, DateOnly month, DateOnly today)
    {
        if (endedOn is not null)
        {
            return RuleDenial.BusinessRule(
                ErrorCodes.OverheadAlreadyCancelled, "This expense is cancelled. Resume it before changing its currency, or add a new one.");
        }

        if (string.Equals(currency, newCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return RuleDenial.Invalid(ErrorCodes.OverheadSameCurrency, "currency", "The expense is already in this currency.");
        }

        return month < MonthStart(today)
            ? RuleDenial.Invalid(ErrorCodes.OverheadMonthInPast, "fromMonth", "Past months keep their cost; choose this month or a later one.")
            : null;
    }

    /// <summary>
    /// A cancelled line can't be resumed while another with its name and group carries it on from the day after it
    /// ends (a change of currency, or it was added again): both would then be charged.
    /// </summary>
    public static RuleDenial? CheckResume(string name, long groupId, DateOnly endedOn, IEnumerable<(string Name, long GroupId, DateOnly StartedOn)> others)
        => others.Any(o => o.StartedOn == endedOn.AddDays(1) && o.GroupId == groupId && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase))
            ? RuleDenial.BusinessRule(ErrorCodes.OverheadCarriedOn, "Another line carries this expense on from the next day. Change that one instead.")
            : null;

    /// <summary>Whether a currency change from <paramref name="month"/> ends the line and adds a new one: it was charged before that month.</summary>
    public static bool CurrencyChangeSplits(DateOnly startedOn, DateOnly month) => startedOn < month;

    public const int MaxGroupName = 60;

    /// <summary>"Other" sorts last; every other category comes before it, a new one after the rest.</summary>
    public const int LastSortOrder = 10_000;

    public static int NextSortOrder(IEnumerable<int> existing)
        => existing.Where(o => o < LastSortOrder).DefaultIfEmpty(0).Max() + 10;

    /// <summary>A category still used by a line, past ones included, stays: removing it would leave them uncategorised.</summary>
    public static RuleDenial? CheckRemoveGroup(int lines)
        => lines > 0
            ? RuleDenial.BusinessRule(ErrorCodes.OverheadGroupInUse, $"{lines} expense{(lines == 1 ? " uses" : "s use")} this category. Move {(lines == 1 ? "it" : "them")} to another one first.")
            : null;
}
