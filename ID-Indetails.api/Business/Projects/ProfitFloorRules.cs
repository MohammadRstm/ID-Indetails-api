using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// A client project's profit may not reach zero. It counts only once the project has an agreed amount (Design's
/// payment, an Execution site's agreed amount); internal work is cost by nature and isn't held to it.
/// </summary>
public static class ProfitFloorRules
{
    /// <summary>At or below this margin a project is getting close to zero.</summary>
    public const decimal WarnMargin = 0.10m;

    public enum Standing
    {
        Fine,
        Close,
        AtZero,
    }

    public static bool Applies(BillingType billingType, string domain, decimal revenue, decimal? projectFee)
        => billingType == BillingType.Client && (domain == DomainCodes.Execution ? projectFee ?? 0 : revenue) > 0;

    /// <param name="profit">Profit on the agreed amount, counting materials not sent yet.</param>
    /// <param name="agreed">The agreed amount, materials not sent yet included.</param>
    public static Standing StandingOf(decimal profit, decimal agreed)
        => profit <= 0 ? Standing.AtZero
            : agreed > 0 && profit / agreed <= WarnMargin ? Standing.Close
            : Standing.Fine;

    /// <summary>The profit as a whole percentage of the agreed amount, for alerts (which never carry amounts).</summary>
    public static int MarginPct(decimal profit, decimal agreed)
        => agreed > 0 ? (int)Math.Round(profit / agreed * 100, MidpointRounding.AwayFromZero) : 0;

    /// <param name="wasPending">Whether the change was still pending when it was decided now.</param>
    public static RuleDenial? CheckPending(bool wasPending)
        => wasPending
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.BlockedChangeDecided, "This change was already forced through or dismissed.");
}
