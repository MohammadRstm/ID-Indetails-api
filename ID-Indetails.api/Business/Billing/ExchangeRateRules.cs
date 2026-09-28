using System.Globalization;
using ID_Indetails.api.Business.Common;
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
}
