using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Billing;

/// <summary>
/// <list type="bullet">
/// <item>A payment is received in its own currency and kept with the rate used and its USD value
/// (amount ÷ rate, to cents). The rate is the one typed in, otherwise the dated rate in force on the
/// day it was received. USD payments use a rate of 1.</item>
/// <item>A payment is either on account (no invoice) or against an issued, live invoice of the same
/// project, and never more than that invoice's balance.</item>
/// <item>A project has at most one live initial payment.</item>
/// <item>Payments are voided with a reason, never deleted or edited. An on-account payment can later be
/// applied to an invoice.</item>
/// </list>
/// </summary>
public static class PaymentRules
{
    public const string BaseCurrency = "USD";

    public static decimal ToUsd(decimal amount, decimal ratePerUsd)
        => Math.Round(amount / ratePerUsd, 2, MidpointRounding.AwayFromZero);

    public static RuleDenial? CheckReceivedOn(DateOnly receivedOn, DateOnly today)
        => receivedOn > today
            ? RuleDenial.Invalid(ErrorCodes.ReceivedDateInFuture, "receivedOn", "A payment can't be received on a future date.")
            : null;

    public static RuleDenial? CheckUsd(decimal amountUsd)
        => amountUsd > 0
            ? null
            : RuleDenial.Invalid(ErrorCodes.PaymentTooSmall, "amount", "The payment is worth less than one cent in US dollars.");

    public static RuleDenial? CheckInvoice(long projectId, long invoiceProjectId, bool isIssued, bool isVoided, decimal balance, decimal amountUsd)
    {
        if (projectId != invoiceProjectId)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "invoiceId", "The invoice belongs to another project.");
        }

        if (InvoiceRules.CheckLive(isIssued, isVoided) is { } denial)
        {
            return denial;
        }

        return amountUsd > balance
            ? RuleDenial.BusinessRule(
                ErrorCodes.PaymentExceedsBalance, "The payment is more than the invoice has left to pay. Record the rest on account.")
            : null;
    }

    /// <summary>Only Execution sites take an initial payment, and at most one live one.</summary>
    public static RuleDenial? CheckInitial(bool isInitial, string projectDomain, bool projectHasLiveInitial)
    {
        if (!isInitial)
        {
            return null;
        }

        if (projectDomain != DomainCodes.Execution)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "isInitial", "Only Execution projects take an initial payment.");
        }

        return projectHasLiveInitial
            ? RuleDenial.BusinessRule(ErrorCodes.InitialPaymentExists, "This project already has an initial payment.")
            : null;
    }

    public static RuleDenial? CheckVoid(bool isVoided)
        => isVoided ? RuleDenial.BusinessRule(ErrorCodes.PaymentAlreadyVoided, "This payment is already void.") : null;

    public static RuleDenial? CheckApply(bool isVoided, long? invoiceId)
    {
        if (CheckVoid(isVoided) is { } denial)
        {
            return denial;
        }

        return invoiceId is null
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.PaymentAlreadyApplied, "Only a payment on account can be applied to an invoice.");
    }
}
