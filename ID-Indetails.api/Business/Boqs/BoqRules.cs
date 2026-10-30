using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Boqs;

/// <summary>
/// Bills of quantities on an Execution client project, and how they price the work:
/// <list type="bullet">
/// <item>A line's final quantity is its quantity plus its waste, to 2 decimals; its total is that × the rate, to cents.
/// The client reads the final quantity, never the exact one nor the waste.</item>
/// <item>A BOQ's subtotal is its lines' totals. It may be adjusted by a percent of it, or to a new total.</item>
/// <item>The work price is the agreed client BOQ's total plus the agreed VOs' totals. Agreeing or withdrawing is the only
/// way it changes. Supervision, TVA and the client's total are worked out from it with the project's rates
/// (<see cref="Projects.ExecutionPricing"/>).</item>
/// <item>Only a VO can lower the price, with a negative rate or a lower total. A client BOQ is one per project; a VO needs
/// the client BOQ agreed first.</item>
/// <item>An agreed BOQ is locked. An internal draft is a private working paper: never numbered, sent or agreed.</item>
/// </list>
/// </summary>
public static class BoqRules
{
    public const int MaxTitleLength = 120;
    public const int MaxReasonLength = 500;
    public const int MaxCodeLength = 20;
    public const int MaxDescriptionLength = 500;
    public const int MaxUnitLength = 20;
    public const int MaxLines = 1000;

    /// <summary>The largest subtotal or total a BOQ can hold, in USD.</summary>
    public const decimal MaxAmount = 999_999_999_999.99m;

    public const decimal MinPercent = -0.99m;
    public const decimal MaxPercent = 1m;

    public const string ClientPrefix = "BQ";
    public const string VoPrefix = "VO";

    private const string CopySuffix = " (copy)";

    /// <summary>Who is asking, and which of the BOQ permissions they hold.</summary>
    /// <param name="CanDraft"><c>boq:create</c>: writes internal drafts.</param>
    /// <param name="CanSeeMoney"><c>financial:view</c>: reads client BOQs and VOs.</param>
    /// <param name="CanManagePricing"><c>project:pricing_manage</c>: writes client BOQs and VOs, and reads every internal draft.</param>
    public sealed record Caller(long UserId, bool CanDraft, bool CanSeeMoney, bool CanManagePricing);

    /// <param name="Quantity">The exact quantity, before waste.</param>
    /// <param name="WasteRate">A fraction: 0.05 adds 5%.</param>
    public sealed record Line(decimal Quantity, decimal WasteRate, decimal Rate);

    public sealed record Totals(decimal Subtotal, decimal Adjustment, decimal Total);

    public static string? PrefixOf(BoqKind kind) => kind switch
    {
        BoqKind.Client => ClientPrefix,
        BoqKind.Vo => VoPrefix,
        _ => null,
    };

    public static string Number(string prefix, int year, int sequence) => $"{prefix}-{year}-{sequence:000}";

    /// <summary>The VO that keeps what a client paid beyond the price when the project is completed.</summary>
    public const string ClosingTitle = "Paid beyond the agreed price";

    public const string ClosingReason = "Kept when the project was completed: the client paid more than the agreed price.";

    public static string DefaultTitle(BoqKind kind) => kind switch
    {
        BoqKind.Client => "Client BOQ",
        BoqKind.Vo => "Variation order",
        _ => "Internal draft",
    };

    /// <summary>The title of a duplicate, cut short enough to still fit.</summary>
    public static string CopyTitle(string title)
        => title.Length + CopySuffix.Length <= MaxTitleLength ? title + CopySuffix : title[..(MaxTitleLength - CopySuffix.Length)] + CopySuffix;

    public static decimal FinalQuantity(decimal quantity, decimal wasteRate) => Round(quantity * (1 + wasteRate));

    public static decimal LineTotal(decimal quantity, decimal wasteRate, decimal rate) => Round(FinalQuantity(quantity, wasteRate) * rate);

    public static Totals TotalsOf(IEnumerable<Line> lines, BoqAdjustmentType adjustment, decimal? value)
    {
        var subtotal = lines.Sum(l => LineTotal(l.Quantity, l.WasteRate, l.Rate));
        var change = adjustment switch
        {
            BoqAdjustmentType.Percent => Round(subtotal * (value ?? 0)),
            BoqAdjustmentType.Total => Round(value ?? 0) - subtotal,
            _ => 0m,
        };

        return new Totals(subtotal, change, subtotal + change);
    }

    /// <summary>What the client is agreed to pay for the work: the agreed client BOQ and the agreed VOs.</summary>
    public static decimal WorkPrice(decimal agreedClientTotal, IEnumerable<decimal> agreedVoTotals) => agreedClientTotal + agreedVoTotals.Sum();

    public static bool CanRead(BoqKind kind, long authorId, Caller caller)
        => kind == BoqKind.Internal
            ? caller.CanManagePricing || (caller.CanDraft && authorId == caller.UserId)
            : caller.CanSeeMoney || caller.CanManagePricing;

    public static bool CanWrite(BoqKind kind, long authorId, Caller caller)
        => kind == BoqKind.Internal
            ? caller.CanManagePricing || (caller.CanDraft && authorId == caller.UserId)
            : caller.CanManagePricing;

    public static bool CanCreate(BoqKind kind, Caller caller) => kind == BoqKind.Internal ? caller.CanDraft : caller.CanManagePricing;

    /// <summary>BOQs exist only on Execution client projects.</summary>
    public static RuleDenial? CheckSite(string domain, BillingType billingType)
        => domain == DomainCodes.Execution && billingType == BillingType.Client
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.BoqExecutionOnly, "Only Execution client projects have BOQs.");

    /// <summary>A variation order changes a price that was agreed: the client BOQ must be agreed first.</summary>
    public static RuleDenial? CheckCanCreate(BoqKind kind, bool clientAgreed)
        => kind == BoqKind.Vo && !clientAgreed
            ? RuleDenial.BusinessRule(ErrorCodes.BoqNotAgreedYet, "A variation order needs the client BOQ to be agreed first.")
            : null;

    public static RuleDenial? CheckEditable(BoqStatus status)
        => status == BoqStatus.Agreed
            ? RuleDenial.BusinessRule(ErrorCodes.BoqLocked, "This BOQ is agreed, so it can't change. Withdraw its agreement first.")
            : null;

    /// <summary>
    /// Whether the copy an editor loaded is the BOQ's latest save. The time is stored to the millisecond (and may be
    /// rounded there), so two times less than a millisecond apart are the same save.
    /// </summary>
    public static bool SameSave(DateTime saved, DateTime loaded)
        => Math.Abs((saved - loaded).Ticks) < TimeSpan.TicksPerMillisecond;

    /// <summary>
    /// What a save leaves: lines within the limit, a reason only on a VO (and there), no negative rate outside a VO, an
    /// adjustment that fits the kind, and totals that fit what is stored.
    /// </summary>
    public static RuleDenial? CheckContent(BoqKind kind, string? reason, BoqAdjustmentType adjustment, decimal? value, IReadOnlyList<Line> lines)
    {
        if (lines.Count > MaxLines)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "lines", $"A BOQ has at most {MaxLines} lines.");
        }

        if (kind == BoqKind.Vo && string.IsNullOrWhiteSpace(reason))
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "reason", "A variation order needs a reason.");
        }

        if (kind != BoqKind.Vo && !string.IsNullOrWhiteSpace(reason))
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "reason", "Only a variation order has a reason.");
        }

        if (kind != BoqKind.Vo && lines.Any(l => l.Rate < 0))
        {
            return RuleDenial.BusinessRule(ErrorCodes.BoqNegativeRate, "Only a variation order can have a negative rate.");
        }

        if (kind != BoqKind.Vo && adjustment == BoqAdjustmentType.Total && value < 0)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "adjustmentValue", "The new total can't be negative.");
        }

        var totals = TotalsOf(lines, adjustment, value);
        return Math.Abs(totals.Subtotal) > MaxAmount || Math.Abs(totals.Total) > MaxAmount
            ? RuleDenial.Invalid(ErrorCodes.ValidationFailed, "lines", "The BOQ's total is too large.")
            : null;
    }

    /// <summary>The client reads client BOQs and VOs, never an internal draft.</summary>
    public static RuleDenial? CheckClientFacing(BoqKind kind)
        => kind == BoqKind.Internal
            ? RuleDenial.BusinessRule(ErrorCodes.BoqInternal, "An internal draft is never sent to the client.")
            : null;

    /// <summary>Internal drafts are never sent; an empty BOQ has nothing to send.</summary>
    public static RuleDenial? CheckSendable(BoqKind kind, int lineCount)
        => CheckClientFacing(kind)
            ?? (lineCount == 0 ? RuleDenial.BusinessRule(ErrorCodes.BoqEmpty, "This BOQ has no lines.") : null);

    /// <param name="clientAgreed">The project has an agreed client BOQ (another one, when this is a client BOQ).</param>
    public static RuleDenial? CheckAgreeable(BoqKind kind, BoqStatus status, int lineCount, bool clientAgreed)
    {
        if (kind == BoqKind.Internal)
        {
            return RuleDenial.BusinessRule(ErrorCodes.BoqInternal, "An internal draft can't be agreed: build the client BOQ from it.");
        }

        if (status == BoqStatus.Agreed)
        {
            return RuleDenial.BusinessRule(ErrorCodes.BoqLocked, "This BOQ is already agreed.");
        }

        if (lineCount == 0)
        {
            return RuleDenial.BusinessRule(ErrorCodes.BoqEmpty, "This BOQ has no lines.");
        }

        return kind switch
        {
            BoqKind.Client when clientAgreed => RuleDenial.BusinessRule(ErrorCodes.BoqAlreadyAgreed, "Another client BOQ of this project is already agreed. Withdraw it first."),
            BoqKind.Vo when !clientAgreed => RuleDenial.BusinessRule(ErrorCodes.BoqNotAgreedYet, "A variation order needs the client BOQ to be agreed first."),
            _ => null,
        };
    }

    public static RuleDenial? CheckWithdrawable(BoqKind kind, BoqStatus status, int agreedVos)
    {
        if (kind == BoqKind.Internal)
        {
            return RuleDenial.BusinessRule(ErrorCodes.BoqInternal, "An internal draft is never agreed.");
        }

        if (status != BoqStatus.Agreed)
        {
            return RuleDenial.BusinessRule(ErrorCodes.BoqNotAgreed, "This BOQ isn't agreed.");
        }

        return kind == BoqKind.Client && agreedVos > 0
            ? RuleDenial.BusinessRule(ErrorCodes.BoqHasAgreedVos, "Variation orders are agreed on top of this BOQ. Withdraw them first.")
            : null;
    }

    /// <summary>Variation orders can lower the price, but never below nothing.</summary>
    public static RuleDenial? CheckWorkPrice(decimal work)
        => work < 0
            ? RuleDenial.BusinessRule(ErrorCodes.BoqPriceBelowZero, "The agreed BOQs would take the work price below zero.")
            : null;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
