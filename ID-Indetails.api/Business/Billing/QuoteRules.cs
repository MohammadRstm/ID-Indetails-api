using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Billing;

/// <summary>
/// A quote tells a Design client what the whole project costs them, TVA included, from the project's agreed payment.
/// Execution has no quotes: its price is built from BOQs.
/// </summary>
public static class QuoteRules
{
    public static string Number(int year, int sequence) => $"QU-{year}-{sequence:000}";

    /// <summary>Only Design client work with an agreed payment can be quoted.</summary>
    public static RuleDenial? CheckQuotable(string domain, BillingType billingType, decimal net)
    {
        if (domain == DomainCodes.Execution)
        {
            return RuleDenial.BusinessRule(ErrorCodes.QuoteNotForExecution, "Execution projects have no quotes: their price comes from the agreed BOQs.");
        }

        if (billingType != BillingType.Client)
        {
            return RuleDenial.BusinessRule(ErrorCodes.QuoteNotPossible, "Internal projects have no client to quote.");
        }

        return net > 0 ? null : RuleDenial.BusinessRule(ErrorCodes.QuoteNotPossible, "Set the project's payment first: a quote needs an amount.");
    }
}
