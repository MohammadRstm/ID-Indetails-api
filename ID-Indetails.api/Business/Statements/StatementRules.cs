using System.Globalization;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Materials;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Statements;

/// <summary>
/// Client statements of materials supplied on an Execution site. A statement is not an invoice and
/// changes nothing that is owed; it is a snapshot of that day's figures.
/// <list type="bullet">
/// <item>New materials are those on no earlier statement. In a detailed statement each can be left out;
/// a total-only statement takes them all.</item>
/// <item>Earlier materials can be included again. They always count in the total; a type with nothing new
/// can be left unlisted, and then joins one "earlier materials" line.</item>
/// <item>Every material a statement shows becomes stated: its price is frozen at the one shown.</item>
/// <item>Numbers are <c>ST-YYYY-NNN</c>, one sequence per year across the company.</item>
/// </list>
/// </summary>
public static class StatementRules
{
    /// <param name="Price">The client price now (frozen for a stated material).</param>
    public sealed record Material(long Id, string Name, decimal? Quantity, string? Unit, decimal Price, bool IsStated, DateTime AddedAt);

    public sealed record Choice(StatementMode Mode, bool IncludeEarlier, IReadOnlySet<long> ExcludedMaterialIds, IReadOnlySet<string> UnlistedTypeKeys);

    public sealed record Line(long MaterialId, int Position, string Description, decimal? Quantity, string? Unit, decimal Price, bool IsEarlier, bool IsListed);

    /// <param name="Quantity">The quantities added up with their unit, when every delivery has the same unit.</param>
    public sealed record TypeRow(string Key, string Label, string? Quantity, int Deliveries, decimal Earlier, decimal New, decimal Total);

    /// <param name="Types">Listed types, by name. A total-only statement lists none.</param>
    /// <param name="HiddenTypes">Earlier types left unlisted, shown as one line.</param>
    public sealed record Document(
        IReadOnlyList<TypeRow> Types,
        int HiddenTypes,
        decimal HiddenTotal,
        decimal EarlierTotal,
        decimal NewTotal,
        decimal Total,
        int Count);

    public sealed record PaymentPosition(decimal Received, decimal Agreed)
    {
        public decimal Remaining => Math.Max(0, Agreed - Received);

        public decimal PaidAhead => Math.Max(0, Received - Agreed);
    }

    public static string Number(int year, int sequence) => $"ST-{year}-{sequence:000}";

    public static (IReadOnlyList<Line> Lines, RuleDenial? Denial) Build(IReadOnlyList<Material> materials, Choice choice)
    {
        var fresh = materials
            .Where(m => !m.IsStated && (choice.Mode == StatementMode.Total || !choice.ExcludedMaterialIds.Contains(m.Id)))
            .ToList();
        var earlier = choice.IncludeEarlier ? materials.Where(m => m.IsStated).ToList() : [];

        var freshTypes = fresh.Select(m => MaterialRules.GroupKey(m.Name)).ToHashSet();
        if (choice.Mode == StatementMode.Detail && choice.UnlistedTypeKeys.Any(freshTypes.Contains))
        {
            return ([], RuleDenial.Invalid(ErrorCodes.ValidationFailed, "unlistedTypeKeys", "A type with new materials on this statement is always listed."));
        }

        if (fresh.Count + earlier.Count == 0)
        {
            return ([], RuleDenial.BusinessRule(ErrorCodes.StatementEmpty, "There are no materials to put on this statement."));
        }

        var lines = fresh.Select(m => (Material: m, IsEarlier: false))
            .Concat(earlier.Select(m => (Material: m, IsEarlier: true)))
            .OrderBy(x => x.Material.AddedAt)
            .ThenBy(x => x.Material.Id)
            .Select((x, i) => new Line(
                x.Material.Id,
                i + 1,
                x.Material.Name,
                x.Material.Quantity,
                x.Material.Unit,
                x.Material.Price,
                x.IsEarlier,
                IsListed: !(x.IsEarlier && choice.Mode == StatementMode.Detail && choice.UnlistedTypeKeys.Contains(MaterialRules.GroupKey(x.Material.Name)))))
            .ToList();

        return (lines, null);
    }

    /// <summary>What the client reads: one row per material type, with what was on earlier statements and what is new.</summary>
    public static Document Summarise(StatementMode mode, IReadOnlyList<Line> lines)
    {
        var types = lines
            .GroupBy(l => MaterialRules.GroupKey(l.Description))
            .Select(g => new
            {
                Row = new TypeRow(
                    g.Key,
                    g.First().Description.Trim(),
                    QuantityOf(g.ToList()),
                    g.Count(),
                    g.Where(l => l.IsEarlier).Sum(l => l.Price),
                    g.Where(l => !l.IsEarlier).Sum(l => l.Price),
                    g.Sum(l => l.Price)),
                Listed = g.Any(l => l.IsListed),
            })
            .ToList();
        var hidden = types.Where(t => !t.Listed).ToList();

        return new Document(
            mode == StatementMode.Total
                ? []
                : types.Where(t => t.Listed).Select(t => t.Row).OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase).ToList(),
            hidden.Count,
            hidden.Sum(t => t.Row.Total),
            lines.Where(l => l.IsEarlier).Sum(l => l.Price),
            lines.Where(l => !l.IsEarlier).Sum(l => l.Price),
            lines.Sum(l => l.Price),
            lines.Count);
    }

    private static string? QuantityOf(IReadOnlyList<Line> lines)
    {
        if (lines.Any(l => l.Quantity is null) || lines.Select(l => l.Unit ?? string.Empty).Distinct().Count() != 1)
        {
            return null;
        }

        var total = Math.Round(lines.Sum(l => l.Quantity!.Value), 3).ToString("0.###", CultureInfo.InvariantCulture);
        return lines[0].Unit is { Length: > 0 } unit ? $"{total} {unit}" : total;
    }
}
