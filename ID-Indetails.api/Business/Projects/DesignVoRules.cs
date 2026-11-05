using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// A Design variation order: lines before TVA that add work (above zero) or take it out (below), numbered in a
/// series of Design's own. Its total is divided between the project's departments, by their shares when it is applied
/// or by hand, and never takes the price below zero.
/// </summary>
public static class DesignVoRules
{
    public const int MaxLines = 50;
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 500;
    public const decimal MaxAmount = 100_000_000m;

    /// <summary>Design's series in the document number table; the number itself reads <c>VO-YYYY-NNN</c>.</summary>
    public const string Series = "DVO";

    public static string Number(int year, int sequence) => $"VO-{year}-{sequence:000}";

    public sealed record Line(string Description, decimal Amount);

    /// <summary>At least one line, each described, with an amount to the cent that isn't zero; a total that isn't zero.</summary>
    public static RuleDenial? CheckLines(IReadOnlyList<Line> lines)
    {
        if (lines.Count is 0 or > MaxLines)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "lines", $"Add between 1 and {MaxLines} lines.");
        }

        if (lines.Any(l => string.IsNullOrWhiteSpace(l.Description)))
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "lines", "Describe each line.");
        }

        if (lines.Any(l => l.Amount == 0 || Math.Abs(l.Amount) > MaxAmount || decimal.Round(l.Amount, 2) != l.Amount))
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "lines", "Each line has an amount to the cent, above zero for more work or below for less.");
        }

        return Total(lines) == 0
            ? RuleDenial.Invalid(ErrorCodes.ValidationFailed, "lines", "The lines add up to nothing: a variation order changes the price.")
            : null;
    }

    public static decimal Total(IEnumerable<Line> lines) => lines.Sum(l => l.Amount);

    /// <summary>
    /// By shares: nothing to give by hand. To departments: an amount for some of the project's departments, each once,
    /// to the cent, adding up to the total.
    /// </summary>
    public static RuleDenial? CheckAllocations(
        DesignVoSplit split, IReadOnlyCollection<long> departments, IReadOnlyList<(long TeamId, decimal Amount)> allocations, decimal total)
    {
        if (split == DesignVoSplit.Shares)
        {
            return allocations.Count == 0
                ? null
                : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "allocations", "Split by the shares, a variation order takes no amounts by hand.");
        }

        if (departments.Count == 0)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "split", "This project has no departments to give amounts to.");
        }

        if (allocations.Count == 0 || allocations.Select(a => a.TeamId).Distinct().Count() != allocations.Count
            || allocations.Any(a => !departments.Contains(a.TeamId)))
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "allocations", "Give amounts to the project's departments, each once.");
        }

        if (allocations.Any(a => decimal.Round(a.Amount, 2) != a.Amount))
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "allocations", "Give each department an amount to the cent.");
        }

        return allocations.Sum(a => a.Amount) == total
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "allocations", "The departments' amounts must add up to the variation order's total.");
    }

    /// <summary>
    /// The total divided by the shares, to the cent: what rounding leaves goes to the department with the biggest share
    /// (the first of them), so the amounts add up to the total exactly. None when the project has no departments.
    /// </summary>
    public static IReadOnlyList<(long TeamId, decimal Amount)> ByShares(decimal total, IReadOnlyList<(long TeamId, decimal Share)> shares)
    {
        if (shares.Count == 0)
        {
            return [];
        }

        var amounts = shares.Select(s => (s.TeamId, Amount: Math.Round(total * s.Share, 2, MidpointRounding.AwayFromZero))).ToList();
        var biggest = shares.Select((s, i) => (s.Share, i)).OrderByDescending(x => x.Share).ThenBy(x => x.i).First().i;
        amounts[biggest] = (amounts[biggest].TeamId, amounts[biggest].Amount + total - amounts.Sum(a => a.Amount));
        return amounts;
    }

    public static RuleDenial? CheckPending(DesignVoStatus status)
        => status == DesignVoStatus.Pending
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.DesignVoNotPending,
                status == DesignVoStatus.Applied ? "This variation order is already part of the price." : "This variation order was cancelled.");

    /// <summary>A cancelled variation order is kept as it was; any other can take TVA on or off.</summary>
    public static RuleDenial? CheckNotCancelled(DesignVoStatus status)
        => status == DesignVoStatus.Cancelled
            ? RuleDenial.BusinessRule(ErrorCodes.DesignVoNotPending, "This variation order was cancelled.")
            : null;

    /// <summary>Less work can't take the price below zero.</summary>
    public static RuleDenial? CheckPrice(decimal price, decimal total)
        => price + total >= 0
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.DesignVoBelowZero, "This would take the project's price below zero.");
}
