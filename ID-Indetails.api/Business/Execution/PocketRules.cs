using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Execution;

/// <summary>
/// Execution's pocket money: a super admin moves part of a site's money in by hand, and a site that runs short asks
/// for money back, which a super admin approves or denies. A site's money here is its profit on its agreed amount (the
/// work price plus supervision), so moving money never takes a site to zero or below.
/// </summary>
public static class PocketRules
{
    public const int MaxNoteLength = 500;

    /// <summary>The margin a request's suggestion brings a site back to; below it a site is getting close to zero.</summary>
    public const decimal TargetMargin = 0.10m;

    /// <summary>What brings the site back to <see cref="TargetMargin"/> on its agreed amount; nothing when it is there already.</summary>
    public static decimal Suggest(decimal agreed, decimal profit)
        => Math.Max(0, Math.Round(TargetMargin * agreed - profit, 2, MidpointRounding.AwayFromZero));

    /// <summary>Only an Execution client site moves money with the pocket.</summary>
    public static RuleDenial? CheckSite(string domain, BillingType billingType)
        => domain == DomainCodes.Execution && billingType == BillingType.Client
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.NotForThisDomain, "Only Execution client sites move money with the pocket money.");

    /// <summary>A contribution leaves the site's profit above zero.</summary>
    public static RuleDenial? CheckContribution(decimal amount, decimal profit)
        => amount < profit
            ? null
            : RuleDenial.BusinessRule(
                ErrorCodes.PocketWouldEmptySite,
                profit > 0
                    ? $"The site's profit is {profit:0.##} USD. Move less than that, so it stays above zero."
                    : "The site has no profit to move into the pocket money.");

    public static RuleDenial? CheckPending(PocketRequestStatus status)
        => status == PocketRequestStatus.Pending
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.PocketRequestDecided, "This request was already decided.");

    public static RuleDenial? CheckBalance(decimal amount, decimal balance)
        => amount <= balance
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.PocketShort, $"The pocket money holds {balance:0.##} USD, less than that.");
}
