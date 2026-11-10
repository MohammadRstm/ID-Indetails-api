using System.Globalization;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Billing;

/// <summary>
/// Exchange rates are dated and only ever added at the end, like pay history: a new rate applies
/// from today or an earlier day, after the currency's latest rate. Payments already recorded keep
/// the rate they were recorded with.
/// </summary>
public static class ExchangeRateRules
{
    public static RuleDenial? Check(DateOnly effectiveFrom, DateOnly today, DateOnly? latestEffectiveFrom)
    {
        if (effectiveFrom > today)
        {
            return RuleDenial.Invalid(ErrorCodes.RateDateInFuture, "effectiveFrom", "A rate can't apply from a future date.");
        }

        return latestEffectiveFrom is { } latest && effectiveFrom <= latest
            ? RuleDenial.BusinessRule(
                ErrorCodes.RateNotAfterLatest,
                $"This currency already has a rate from {latest.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}. A new rate must apply after that date.")
            : null;
    }

    /// <summary>
    /// Whether the daily feed's rate may be added: above zero, and no further than <paramref name="maxChange"/> (0.10 is
    /// 10%) from the last rate, so a glitch in the source can't convert a day's money at a wrong rate that can't be edited.
    /// The first rate of a currency has nothing to compare with.
    /// </summary>
    public static bool IsFeedRateUsable(decimal rate, decimal? lastRate, decimal maxChange)
        => rate > 0 && (lastRate is not { } last || Math.Abs(rate - last) <= last * maxChange);

    /// <summary>
    /// A cost in another currency needs a rate in force on its day, on a Design project as on a site: money leaves out what
    /// it can't convert, so without one the cost would never reach profit or the profit floor.
    /// </summary>
    public static RuleDenial? CheckConvertible(IEnumerable<(string Currency, DateOnly Day)> costs, ExchangeRateTable rates)
    {
        foreach (var (currency, day) in costs)
        {
            if (rates.ToUsd(1m, currency, day) is null)
            {
                return RuleDenial.BusinessRule(ErrorCodes.ExchangeRateMissing,
                    $"There is no {currency} exchange rate in force on {day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}. Add one first, or record the expense in USD.");
            }
        }

        return null;
    }
}
