using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Execution;

/// <summary>
/// Supervision taken: the company withdrawing some of an Execution site's supervision fee from the project account.
/// It is not a cost, so it never moves the site's profit (and has no profit floor); it only lowers the cash in the
/// account and what is left to take. No more than is left of what is due can be taken.
/// </summary>
public static class SupervisionRules
{
    public const int MaxNoteLength = 500;

    /// <summary>The column holds 14 digits, 2 of them decimals.</summary>
    public const decimal MaxAmount = 999_999_999_999.99m;

    /// <summary>Only an Execution client site has supervision to take and an account to take it from.</summary>
    public static RuleDenial? CheckSite(string domain, BillingType billingType)
        => domain == DomainCodes.Execution && billingType == BillingType.Client
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.NotForThisDomain, "Only Execution client sites have supervision taken and account figures.");

    public static RuleDenial? CheckTakenOn(DateOnly takenOn, DateOnly today)
        => takenOn > today
            ? RuleDenial.Invalid(ErrorCodes.TakenDateInFuture, "takenOn", "Supervision can't be recorded as taken on a future date.")
            : null;

    /// <summary>What is still to take of the supervision due; negative only for what was taken before the work price fell.</summary>
    public static decimal Left(decimal due, decimal taken) => due - taken;

    public static RuleDenial? CheckAmount(decimal amount, decimal left)
        => amount <= left
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "amountUsd",
                left > 0 ? $"Only {left:N2} USD of the supervision is left to take." : "Nothing of the supervision is left to take.");
}
