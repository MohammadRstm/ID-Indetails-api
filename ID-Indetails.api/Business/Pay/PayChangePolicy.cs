using System.Globalization;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Pay;

/// <summary>
/// When a pay change may apply. Pay history is only ever added to at the end:
/// <list type="bullet">
/// <item>A change applies from today or an earlier day, never a future one.</item>
/// <item>It must apply after the latest existing change. Anything else would either rewrite
/// or silently cancel later changes, so a mistake is corrected with a new, later change.</item>
/// </list>
/// A change dated in the past still re-prices work done since that day, because salaries and
/// labour costs are always calculated from the history.
/// </summary>
public static class PayChangePolicy
{
    public static RuleDenial? Check(DateOnly effectiveFrom, DateOnly companyToday, DateOnly? latestEffectiveFrom)
    {
        if (effectiveFrom > companyToday)
        {
            return RuleDenial.Invalid(ErrorCodes.PayDateInFuture, "effectiveFrom", "A pay change cannot apply from a future date.");
        }

        return latestEffectiveFrom is { } latest && effectiveFrom <= latest
            ? RuleDenial.BusinessRule(
                ErrorCodes.PayChangeNotAfterLatest,
                $"Pay already changed on {latest.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}. A new change must apply after that date.")
            : null;
    }
}
