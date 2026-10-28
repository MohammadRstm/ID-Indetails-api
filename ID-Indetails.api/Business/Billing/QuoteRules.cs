using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Billing;

/// <summary>
/// A quote tells a client what the whole project costs them, TVA included. It is made from the project's agreed
/// payment (Design) or agreed amount before materials (Execution, whose materials are billed as they are delivered).
/// </summary>
public static class QuoteRules
{
    public static string Number(int year, int sequence) => $"QU-{year}-{sequence:000}";

    /// <summary>What the quote is for, before TVA: Design's payment, or an Execution site's agreed amount.</summary>
    public static decimal NetOf(string domain, decimal revenue, decimal? projectFee)
        => domain == DomainCodes.Execution ? projectFee ?? 0 : revenue;

    /// <summary>Only client work with an agreed amount can be quoted.</summary>
    public static RuleDenial? CheckQuotable(BillingType billingType, decimal net)
    {
        if (billingType != BillingType.Client)
        {
            return RuleDenial.BusinessRule(ErrorCodes.QuoteNotPossible, "Internal projects have no client to quote.");
        }

        return net > 0 ? null : RuleDenial.BusinessRule(ErrorCodes.QuoteNotPossible, "Set the project's payment first: a quote needs an amount.");
    }
}
