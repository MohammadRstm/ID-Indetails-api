using System.Globalization;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Billing;

/// <summary>An invoice's status. Calculated, never stored.</summary>
public enum InvoiceStatus
{
    Draft,
    Sent,
    Partial,
    Paid,
    Void,
}

/// <summary>
/// Invoice lifecycle: a <b>draft</b> is edited or discarded freely; <b>issuing</b> gives it the next
/// number of its domain and year and a due date; an issued invoice can be delivered any number of
/// times, and <b>voided</b> (with a reason) while no live payment is attached. Status and balance
/// come from the live payments, compared exactly (no rounding tolerance).
/// </summary>
public static class InvoiceRules
{
    public static InvoiceStatus Status(bool isVoided, bool isIssued, decimal total, decimal paid)
    {
        if (isVoided)
        {
            return InvoiceStatus.Void;
        }

        if (!isIssued)
        {
            return InvoiceStatus.Draft;
        }

        return paid >= total ? InvoiceStatus.Paid : paid > 0 ? InvoiceStatus.Partial : InvoiceStatus.Sent;
    }

    public static decimal Balance(decimal total, decimal paid) => Math.Max(0, total - paid);

    /// <summary>
    /// What is left to pay in the invoice's own currency. Payments are summed in USD, so the comparison is in USD (the
    /// total at its issue day's rate); the rest goes back into the invoice's currency at that same rate.
    /// </summary>
    public static decimal BalanceIn(decimal total, decimal totalUsd, decimal paidUsd)
    {
        var leftUsd = Balance(totalUsd, paidUsd);
        return total == totalUsd || totalUsd == 0 ? leftUsd : Math.Round(leftUsd * total / totalUsd, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Sent or part-paid, with a due date before today.</summary>
    public static bool IsOverdue(InvoiceStatus status, DateOnly? dueOn, DateOnly today)
        => status is InvoiceStatus.Sent or InvoiceStatus.Partial && dueOn is { } due && due < today;

    /// <summary><c>PREFIX-YYYY-NNN</c>, e.g. HL-2026-007.</summary>
    public static string Number(string prefix, int year, int sequence)
        => string.Create(CultureInfo.InvariantCulture, $"{prefix}-{year}-{sequence:000}");

    public static RuleDenial? CheckBillable(BillingType billingType, long? clientId)
        => billingType == BillingType.Client && clientId is not null
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.ProjectNotBillable, "Only client projects with a client can be invoiced.");

    public static RuleDenial? CheckDraft(bool isIssued, bool isVoided)
        => isIssued || isVoided
            ? RuleDenial.BusinessRule(ErrorCodes.InvoiceNotDraft, "Only a draft invoice can be changed or discarded. Void and reissue it instead.")
            : null;

    /// <summary>
    /// The day an overdue invoice's next reminder is due: <paramref name="firstAfterDays"/> after its due date, then
    /// <paramref name="everyDays"/> after the last one sent; null once <paramref name="max"/> have been sent.
    /// </summary>
    public static DateOnly? NextReminderOn(DateOnly dueOn, int sent, DateOnly? lastSentOn, int firstAfterDays, int everyDays, int max)
        => sent >= max ? null : lastSentOn is { } last ? last.AddDays(everyDays) : dueOn.AddDays(firstAfterDays);

    /// <summary>Auto invoice splits what is left to bill into 2 to this many invoices.</summary>
    public const int MaxPlanInvoices = 24;

    public static RuleDenial? CheckSendOn(DateOnly sendOn, DateOnly today)
        => sendOn < today
            ? RuleDenial.Invalid(ErrorCodes.SendDateInPast, "sendOn", "A scheduled invoice goes out today or later.")
            : null;

    public static RuleDenial? CheckIssueDate(DateOnly issuedOn, DateOnly today)
        => issuedOn > today
            ? RuleDenial.Invalid(ErrorCodes.IssueDateInFuture, "issuedOn", "An invoice can't be issued with a future date.")
            : null;

    /// <summary>Delivering and recording payments need an issued, live invoice.</summary>
    public static RuleDenial? CheckLive(bool isIssued, bool isVoided)
    {
        if (isVoided)
        {
            return RuleDenial.BusinessRule(ErrorCodes.InvoiceVoided, "This invoice is void.");
        }

        return isIssued ? null : RuleDenial.BusinessRule(ErrorCodes.InvoiceNotIssued, "Issue the invoice first.");
    }

    public static RuleDenial? CheckVoid(bool isIssued, bool isVoided, bool hasLivePayments)
    {
        if (CheckLive(isIssued, isVoided) is { } denial)
        {
            return denial;
        }

        return hasLivePayments
            ? RuleDenial.BusinessRule(ErrorCodes.InvoiceHasPayments, "Void the payments recorded against this invoice first.")
            : null;
    }
}
