using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Boqs;

/// <summary>
/// How far an Execution site is by value (the owner's decision Q7): the value executed ÷ the agreed value of its work.
/// <list type="bullet">
/// <item>Site staff and pricing managers record, per BOQ line and day, the quantity executed so far (cumulative, in the
/// line's unit). The latest record on or before a day is the line's executed quantity on that day.</item>
/// <item>Only the work lines of the agreed client BOQ and the agreed VOs count. Permits are not work; the VOs made at
/// completion (excused, written off, an overpayment kept) price money, not work, so neither counts or takes progress. A
/// line with a negative rate takes work away: it lowers the agreed value and has nothing to execute.</item>
/// <item>A line's quantity is its final quantity (with waste), the one the client is priced on. Up to twice it may be
/// recorded, so an overrun shows (progress past 1) while a typo is caught.</item>
/// <item>Value is net of TVA, work only (no supervision): the line's rate with its BOQ's adjustment spread over it, so a
/// BOQ fully executed is worth its agreed total. Value executed = executed × that rate, not capped at the line's quantity.</item>
/// </list>
/// </summary>
public static class BoqProgressRules
{
    /// <summary>The most lines one request may record.</summary>
    public const int MaxLines = 200;

    public const int MaxNoteLength = 500;

    /// <summary>The most that may be recorded on a line, as a multiple of its quantity.</summary>
    public const decimal MaxOverrun = 2m;

    /// <summary>The records the site's progress shows, newest first.</summary>
    public const int HistoryLimit = 50;

    /// <summary>A BOQ line as progress sees it, with what it needs of its BOQ.</summary>
    /// <param name="Quantity">The exact quantity, before waste.</param>
    /// <param name="BoqSubtotal">The BOQ's work lines' totals, before its adjustment.</param>
    /// <param name="BoqTotal">The BOQ's work after its adjustment.</param>
    public sealed record Line(
        long Id,
        long BoqId,
        BoqKind Kind,
        BoqStatus Status,
        string BoqTitle,
        decimal BoqSubtotal,
        decimal BoqTotal,
        BoqLineNature Nature,
        decimal Quantity,
        decimal WasteRate,
        decimal Rate);

    /// <summary>A line's executed quantity as asked to be recorded.</summary>
    public sealed record Entry(long LineId, decimal ExecutedQuantity);

    /// <param name="Quantity">The final quantity, with waste.</param>
    /// <param name="Executed">The latest quantity recorded; null when none was, and on a line that takes no progress.</param>
    /// <param name="Progress">Executed ÷ quantity, a fraction to four places (may pass 1); null without a record or a quantity.</param>
    /// <param name="UnitPrice">The rate with its BOQ's adjustment spread over it, to four places, net of TVA.</param>
    /// <param name="AgreedValue">Quantity × that rate, to cents; negative on a line that takes work away.</param>
    /// <param name="ValueExecuted">Executed × that rate, to cents; 0 without a record.</param>
    public sealed record LineFigures(
        Line Line,
        bool TakesProgress,
        decimal Quantity,
        decimal? Executed,
        decimal? Progress,
        decimal UnitPrice,
        decimal AgreedValue,
        decimal ValueExecuted);

    /// <param name="Recorded">Some line has a record: until then the site has no progress by value.</param>
    /// <param name="Progress">Value executed ÷ agreed value, a fraction to four places (may pass 1); null while the agreed value is 0 or less.</param>
    public sealed record Sheet(IReadOnlyList<LineFigures> Lines, decimal AgreedValue, decimal ValueExecuted, decimal? Progress, bool Recorded);

    /// <summary>A VO, or a client BOQ, made when the project was completed: it prices money already received or forgiven, not work.</summary>
    public static bool IsSettlement(BoqKind kind, string title)
        => kind != BoqKind.Internal && title is BoqRules.ExcusedTitle or BoqRules.WrittenOffTitle or BoqRules.ClosingTitle;

    /// <summary>The line is work of an agreed client BOQ or VO that prices work: it counts towards the agreed value.</summary>
    public static bool Counts(Line line)
        => line.Status == BoqStatus.Agreed
            && line.Kind is BoqKind.Client or BoqKind.Vo
            && !IsSettlement(line.Kind, line.BoqTitle)
            && line.Nature == BoqLineNature.Work;

    /// <summary>A counted line that adds work: one with a negative rate takes work away, so nothing on it is executed.</summary>
    public static bool TakesProgress(Line line) => Counts(line) && line.Rate >= 0;

    /// <summary>The quantity progress is measured against: the final quantity, with waste.</summary>
    public static decimal QuantityOf(Line line) => BoqRules.FinalQuantity(line.Quantity, line.WasteRate);

    /// <summary>The most that may be recorded on a line of <paramref name="quantity"/>.</summary>
    public static decimal MostExecuted(decimal quantity) => quantity * MaxOverrun;

    /// <summary>The rate with the BOQ's adjustment spread over it, unrounded: a BOQ fully executed is worth its total.</summary>
    public static decimal AdjustedRate(decimal rate, decimal subtotal, decimal total) => subtotal == 0 ? rate : rate * total / subtotal;

    /// <summary>Progress is recorded up to today, in the company's time zone.</summary>
    public static RuleDenial? CheckDay(DateOnly recordedOn, DateOnly today)
        => recordedOn > today
            ? RuleDenial.Invalid(ErrorCodes.ValidationFailed, "recordedOn", "Progress is recorded up to today, never for a day still to come.")
            : null;

    /// <summary>
    /// The lines being recorded against the site's: each once, each a line that takes progress, and none more than twice
    /// its quantity.
    /// </summary>
    /// <param name="lines">The lines of the site's agreed BOQs and VOs, by id.</param>
    public static RuleDenial? CheckEntries(IReadOnlyList<Entry> entries, IReadOnlyDictionary<long, Line> lines)
    {
        if (entries.Select(e => e.LineId).Distinct().Count() != entries.Count)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "lines", "Record each line once.");
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var field = $"lines[{i}].boqLineId";
            if (!lines.TryGetValue(entries[i].LineId, out var line) || line.Status != BoqStatus.Agreed || line.Kind == BoqKind.Internal)
            {
                return RuleDenial.Invalid(ErrorCodes.BoqProgressLineInvalid, field, "That line isn't on an agreed BOQ or VO of this site.");
            }

            if (IsSettlement(line.Kind, line.BoqTitle))
            {
                return RuleDenial.Invalid(ErrorCodes.BoqProgressLineInvalid, field,
                    "That line was made when the project was completed: it prices money, not work, so it has no progress.");
            }

            if (line.Nature != BoqLineNature.Work)
            {
                return RuleDenial.Invalid(ErrorCodes.BoqProgressLineInvalid, field, "A permit isn't work on site, so it has no progress.");
            }

            if (line.Rate < 0)
            {
                return RuleDenial.Invalid(ErrorCodes.BoqProgressLineInvalid, field, "That line takes work away, so nothing on it is executed.");
            }

            var most = MostExecuted(QuantityOf(line));
            if (entries[i].ExecutedQuantity > most)
            {
                return RuleDenial.Invalid(ErrorCodes.ValidationFailed, $"lines[{i}].executedQuantity",
                    $"That's more than twice the line's quantity: at most {most.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}. Check the figure.");
            }
        }

        return null;
    }

    /// <summary>One counted line's figures, from its latest executed quantity (null when none was recorded).</summary>
    public static LineFigures FiguresOf(Line line, decimal? executed)
    {
        var quantity = QuantityOf(line);
        var rate = AdjustedRate(line.Rate, line.BoqSubtotal, line.BoqTotal);
        var takes = TakesProgress(line);
        var done = takes ? executed : null;
        return new LineFigures(
            line,
            takes,
            quantity,
            done,
            done is { } d && quantity > 0 ? Fraction(d / quantity) : null,
            Math.Round(rate, 4, MidpointRounding.AwayFromZero),
            Cents(quantity * rate),
            done is { } e ? Cents(e * rate) : 0m);
    }

    /// <summary>
    /// The site's progress by value: each counted line's figures (in the order given), the agreed value (the counted work,
    /// net of TVA, within cents of the work price of those BOQs), the value executed, and progress.
    /// </summary>
    /// <param name="lines">The lines of the site's agreed BOQs and VOs; those that don't count are left out.</param>
    /// <param name="executed">Each line's latest executed quantity on the day, by line id.</param>
    public static Sheet Of(IEnumerable<Line> lines, IReadOnlyDictionary<long, decimal> executed)
    {
        var figures = lines.Where(Counts)
            .Select(l => FiguresOf(l, executed.TryGetValue(l.Id, out var q) ? q : null))
            .ToList();
        var agreed = figures.Sum(f => f.AgreedValue);
        var done = figures.Sum(f => f.ValueExecuted);
        return new Sheet(figures, agreed, done, agreed > 0 ? Fraction(done / agreed) : null, figures.Any(f => f.Executed is not null));
    }

    private static decimal Fraction(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
