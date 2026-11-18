using System.Text.RegularExpressions;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// A project's colour (its <c>tint</c>), which its tile and the work logged on it are drawn in: one of the palette's
/// thirty deep colours (decided 18 November 2026, picked from Coolors' elegant palettes so no two are close). Each
/// business uses the same thirty on its own: a new project gets one its business's active and on-hold projects don't
/// have, and once all are taken they are used again. Whoever edits a project picks another free one, or swaps with the
/// project that has it.
/// </summary>
public static partial class ProjectColorRules
{
    public const string ColorMessage = "The colour must be a hex colour like #1F6FEB.";

    /// <summary>
    /// In hue order, which the picker shows. Each reads with a white icon on it (contrast 3:1 or more). Named for Proton.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Palette =
    [
        ("raspberry", "#A53860"), ("rose", "#DA627D"), ("cocoa", "#3D2C2E"), ("burgundy", "#6E0D25"), ("dusty rose", "#A26769"),
        ("oxblood", "#81171B"), ("coral", "#FE5F55"), ("terracotta", "#C75146"), ("brick", "#AD2E24"), ("chestnut", "#6A381F"),
        ("copper", "#DA7422"), ("taupe", "#6F5E53"), ("walnut", "#8E6E53"), ("ochre", "#A59132"), ("olive", "#625834"),
        ("moss", "#48483A"), ("sage", "#808F85"), ("jade", "#2A7F62"), ("forest", "#26413C"), ("lagoon", "#407076"),
        ("petrol", "#0B525B"), ("turquoise", "#1B9AAA"), ("slate", "#4C5760"), ("steel", "#748CAB"), ("navy", "#1D2D44"),
        ("dusk", "#4B4E6D"), ("storm", "#6A6B83"), ("aubergine", "#4D194D"), ("mulberry", "#6D466B"), ("heather", "#A28F9D"),
    ];

    /// <summary>#RRGGBB, or #RRGGBBAA as older projects may have.</summary>
    public static bool IsColor(string? value) => value is not null && ColorFormat().IsMatch(value.Trim());

    [GeneratedRegex("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex ColorFormat();

    public static string? HexOf(string name)
        => Palette.Where(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).Select(c => c.Hex).FirstOrDefault();

    public static string? NameOf(string? hex)
        => Palette.Where(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase)).Select(c => c.Name).FirstOrDefault();

    /// <summary>
    /// A palette colour no open project has, at random. When every one is taken, one of those the fewest open
    /// projects have.
    /// </summary>
    public static string Pick(IEnumerable<string> openProjectTints, Random random)
    {
        var used = openProjectTints
            .GroupBy(t => t.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var counts = Palette.Select(c => (c.Hex, Count: used.GetValueOrDefault(c.Hex))).ToList();
        var fewest = counts.Min(c => c.Count);
        var candidates = counts.Where(c => c.Count == fewest).ToList();
        return candidates[random.Next(candidates.Count)].Hex;
    }
}
