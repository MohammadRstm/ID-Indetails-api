using System.Text.RegularExpressions;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// A project's colour (its <c>tint</c>), which its tile and the work logged on it are drawn in. A new project gets one
/// of the palette's at random, avoiding the colours of the active and on-hold projects while any is free. Anyone who
/// edits the project may then pick any colour.
/// </summary>
public static partial class ProjectColorRules
{
    public const string ColorMessage = "The colour must be a hex colour like #1F6FEB.";

    /// <summary>Each reads with a white icon on it and shows on the dark theme too. Named for Proton.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Palette =
    [
        ("blue", "#2F6FEB"), ("indigo", "#5B5BD6"), ("violet", "#8B5CF6"), ("purple", "#9D3FC7"),
        ("magenta", "#C026A3"), ("pink", "#D6336C"), ("coral", "#E0603F"), ("orange", "#D9730D"),
        ("amber", "#B7791F"), ("olive", "#7C8A16"), ("green", "#2F9E44"), ("emerald", "#0F9F75"),
        ("teal", "#0E8A84"), ("cyan", "#0B8DB8"), ("slate", "#5F7085"), ("brown", "#9C6644"),
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
