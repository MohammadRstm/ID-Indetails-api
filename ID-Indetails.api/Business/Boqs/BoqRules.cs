using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Boqs;

/// <summary>
/// Bills of quantities on an Execution client project, and how they price the work:
/// <list type="bullet">
/// <item>A line's final quantity is its quantity plus its waste, to 2 decimals; its total is that × the rate, to cents.
/// The client reads the final quantity, never the exact one nor the waste.</item>
/// <item>A line is work or a permit. A BOQ's subtotal is its work lines' totals; it may be adjusted by a percent of it, or
/// to a new total. Its permits are its permit lines' totals, never adjusted.</item>
/// <item>The work price is the agreed client BOQ's total plus the agreed VOs' totals, and the permits theirs likewise.
/// Agreeing or withdrawing is the only way they change. Supervision and TVA are worked out on the work price alone, with
/// the project's rates; the permits are charged as they are (<see cref="Projects.ExecutionPricing"/>).</item>
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
    /// <param name="IsSiteOwner">The owner of the site the BOQ belongs to: they see and edit its submitted drafts.</param>
    public sealed record Caller(long UserId, bool CanDraft, bool CanSeeMoney, bool CanManagePricing, bool IsSiteOwner = false);

    /// <summary>A line as an internal draft was submitted, or last kept by its author.</summary>
    public sealed record SubmittedLine(long Id, BoqLineNature Nature, string? Code, string Description, decimal Quantity, decimal WasteRate, string? Unit, decimal Rate);

    /// <summary>What changed in one line since its draft was submitted: a value before and after, as typed.</summary>
    public sealed record FieldChange(string Field, string? Before, string? After);

    /// <param name="Kind">added, removed or changed.</param>
    /// <param name="LineId">Null for a removed line, which no longer exists.</param>
    public sealed record LineChange(string Kind, long? LineId, string Description, IReadOnlyList<FieldChange> Fields);

    /// <param name="Quantity">The exact quantity, before waste.</param>
    /// <param name="WasteRate">A fraction: 0.05 adds 5%.</param>
    public sealed record Line(decimal Quantity, decimal WasteRate, decimal Rate, BoqLineNature Nature = BoqLineNature.Work);

    /// <param name="Subtotal">The work lines' totals.</param>
    /// <param name="Total">The work after the adjustment: what supervision and TVA are worked out on.</param>
    /// <param name="Permits">The permit lines' totals, as they are.</param>
    public sealed record Totals(decimal Subtotal, decimal Adjustment, decimal Total, decimal Permits);

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

    /// <summary>The VO, or the Design payment change, that lowers the price to what was received when what was owed is excused.</summary>
    public const string ExcusedTitle = "Excused at completion";

    public const string ExcusedReason = "Excused when the project was completed: the client no longer owes what was left to pay.";

    /// <summary>The same, when what was owed is written off as lost.</summary>
    public const string WrittenOffTitle = "Unpaid, written off at completion";

    public const string WrittenOffReason = "Written off when the project was completed: the client never paid what was left, and that revenue is lost.";

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

    /// <summary>The work and the permits apart: the adjustment is the work's, the permits are charged as they are.</summary>
    public static Totals TotalsOf(IReadOnlyCollection<Line> lines, BoqAdjustmentType adjustment, decimal? value)
    {
        decimal SumOf(BoqLineNature nature) => lines.Where(l => l.Nature == nature).Sum(l => LineTotal(l.Quantity, l.WasteRate, l.Rate));
        var subtotal = SumOf(BoqLineNature.Work);
        var change = adjustment switch
        {
            BoqAdjustmentType.Percent => Round(subtotal * (value ?? 0)),
            BoqAdjustmentType.Total => Round(value ?? 0) - subtotal,
            _ => 0m,
        };

        return new Totals(subtotal, change, subtotal + change, SumOf(BoqLineNature.Permit));
    }

    /// <summary>
    /// What the client is agreed to pay, the work or the permits: the agreed client BOQ's and the agreed VOs'.
    /// </summary>
    public static decimal WorkPrice(decimal agreedClientTotal, IEnumerable<decimal> agreedVoTotals) => agreedClientTotal + agreedVoTotals.Sum();

    /// <summary>
    /// An internal draft is its author's alone until they submit it; submitted, the site's pricing managers and owner see
    /// it too. Client BOQs and VOs are for whoever sees money or manages pricing.
    /// </summary>
    public static bool CanRead(BoqKind kind, long authorId, bool submitted, Caller caller)
        => kind == BoqKind.Internal
            ? (caller.CanDraft && authorId == caller.UserId) || (submitted && (caller.CanManagePricing || caller.IsSiteOwner))
            : caller.CanSeeMoney || caller.CanManagePricing;

    /// <summary>A private draft is changed by its author; a submitted one by the site's pricing managers and owner, while its author only reads it.</summary>
    public static bool CanWrite(BoqKind kind, long authorId, bool submitted, Caller caller)
        => kind == BoqKind.Internal
            ? submitted ? caller.CanManagePricing || caller.IsSiteOwner : caller.CanDraft && authorId == caller.UserId
            : caller.CanManagePricing;

    /// <summary>Only its author deletes an internal draft, and only while it is private.</summary>
    public static bool CanDelete(BoqKind kind, long authorId, bool submitted, Caller caller)
        => kind == BoqKind.Internal ? !submitted && caller.CanDraft && authorId == caller.UserId : caller.CanManagePricing;

    /// <summary>
    /// The sites a BOQ author reaches without seeing Execution: its active client sites. They see nothing there but their
    /// own drafts.
    /// </summary>
    public static bool IsAuthorSite(string domain, BillingType billingType, ProjectStatus status)
        => domain == DomainCodes.Execution && billingType == BillingType.Client && status == ProjectStatus.Active;

    public static RuleDenial? CheckSubmittable(BoqKind kind, bool submitted, int lineCount)
    {
        if (kind != BoqKind.Internal)
        {
            return RuleDenial.BusinessRule(ErrorCodes.BoqNotInternal, "Only an internal draft is submitted to its site.");
        }

        if (submitted)
        {
            return RuleDenial.BusinessRule(ErrorCodes.BoqAlreadySubmitted, "This draft is already submitted.");
        }

        return lineCount == 0 ? RuleDenial.BusinessRule(ErrorCodes.BoqEmpty, "Add at least one line before submitting it.") : null;
    }

    public static RuleDenial? CheckSubmitted(BoqKind kind, bool submitted)
        => kind == BoqKind.Internal && submitted ? null : RuleDenial.BusinessRule(ErrorCodes.BoqNotSubmitted, "This draft isn't submitted.");

    /// <summary>
    /// What others changed in a submitted draft since it was submitted (or last kept): lines matched by id, added, removed,
    /// or changed field by field. Empty when nothing changed; null when no copy was kept to compare against.
    /// </summary>
    public static IReadOnlyList<LineChange>? Changes(IReadOnlyList<SubmittedLine>? submitted, IReadOnlyList<SubmittedLine> current)
    {
        if (submitted is null)
        {
            return null;
        }

        var before = submitted.ToDictionary(l => l.Id);
        var changes = new List<LineChange>();
        foreach (var now in current)
        {
            if (!before.TryGetValue(now.Id, out var was))
            {
                changes.Add(new LineChange("added", now.Id, now.Description, []));
                continue;
            }

            var fields = new List<FieldChange>();
            void Compare(string field, string? a, string? b)
            {
                if (a != b)
                {
                    fields.Add(new FieldChange(field, a, b));
                }
            }

            Compare("code", was.Code, now.Code);
            Compare("description", was.Description, now.Description);
            Compare("quantity", Text(was.Quantity), Text(now.Quantity));
            Compare("wasteRate", Text(was.WasteRate), Text(now.WasteRate));
            Compare("unit", was.Unit, now.Unit);
            Compare("rate", Text(was.Rate), Text(now.Rate));
            Compare("nature", was.Nature.ToString(), now.Nature.ToString());
            if (fields.Count > 0)
            {
                changes.Add(new LineChange("changed", now.Id, now.Description, fields));
            }
        }

        var still = current.Select(l => l.Id).ToHashSet();
        changes.AddRange(submitted.Where(l => !still.Contains(l.Id)).Select(l => new LineChange("removed", null, l.Description, [])));
        return changes;
    }

    private static string Text(decimal value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

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
        return Math.Abs(totals.Subtotal) > MaxAmount || Math.Abs(totals.Total) > MaxAmount || Math.Abs(totals.Permits) > MaxAmount
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

    /// <summary>Variation orders can lower the price, but never below nothing: neither the work nor the permits.</summary>
    public static RuleDenial? CheckWorkPrice(decimal work, decimal permits)
        => work < 0 ? RuleDenial.BusinessRule(ErrorCodes.BoqPriceBelowZero, "The agreed BOQs would take the work price below zero.")
            : permits < 0 ? RuleDenial.BusinessRule(ErrorCodes.BoqPriceBelowZero, "The agreed BOQs would take the permits below zero.")
            : null;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
