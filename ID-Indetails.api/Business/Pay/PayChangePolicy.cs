using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Pay;

/// <summary>
/// When a pay change may apply.
/// <list type="bullet">
/// <item>From today or any earlier day, closed months included, never a future one.</item>
/// <item>A change dated D replaces every active entry from D onward (they are kept, marked superseded), so a mistake is
/// corrected with a change dated on its day, and the entries before D stay.</item>
/// <item>A change of currency starts on the 1st of a month, so every month is paid in one currency.</item>
/// </list>
/// Everything worked out from pay follows the new history at once: nothing is stored but an advance's settlement.
/// </summary>
public static class PayChangePolicy
{
    /// <param name="currencyBefore">The currency of the active entry in force the day before <paramref name="effectiveFrom"/>; null when there is none.</param>
    public static RuleDenial? Check(DateOnly effectiveFrom, DateOnly companyToday, string currency, string? currencyBefore)
    {
        if (effectiveFrom > companyToday)
        {
            return RuleDenial.Invalid(ErrorCodes.PayDateInFuture, "effectiveFrom", "A pay change cannot apply from a future date.");
        }

        return currencyBefore is not null && !string.Equals(currency, currencyBefore, StringComparison.OrdinalIgnoreCase) && effectiveFrom.Day != 1
            ? RuleDenial.Invalid(ErrorCodes.PayCurrencyChangeNotFirst, "effectiveFrom", "A change of currency starts on the 1st of a month.")
            : null;
    }
}
