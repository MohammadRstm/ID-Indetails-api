using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// A project's codes. The project code is <c>ID</c>, the year (2 digits), the quarter and a number that restarts every
/// quarter in each business: ID26Q4001. The building code is short, usually the initials of the project's name: VK.
/// Together they make the full code, ID26Q4001-VK.
/// </summary>
public static partial class ProjectCodeRules
{
    public const int MaxProjectCodeLength = 16;
    public const int MaxBuildingCodeLength = 10;

    /// <summary>The building code of a name without a Latin letter or a digit to take.</summary>
    public const string NoInitials = "X";

    public const string ProjectCodeMessage = "Use the form ID26Q4001: ID, the year, Q and the quarter, then 3 digits.";
    public const string BuildingCodeMessage = "Use 1 to 10 letters (A to Z) or digits.";

    /// <summary>Not counted in the initials ("Beit el Hana" is BH). "A" is counted: it is a block's name as often as a word.</summary>
    private static readonly HashSet<string> SkippedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "of", "the", "and", "an", "de", "du", "des", "la", "le", "el", "al",
    };

    [GeneratedRegex("^ID[0-9]{2}Q[1-4][0-9]{3}$")]
    private static partial Regex ProjectCodeFormat();

    [GeneratedRegex("^[A-Z0-9]{1,10}$")]
    private static partial Regex BuildingCodeFormat();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();

    /// <summary>Codes are kept trimmed and in capitals, whatever was typed.</summary>
    public static string Clean(string code) => code.Trim().ToUpperInvariant();

    public static bool IsProjectCode(string? code) => code is not null && ProjectCodeFormat().IsMatch(Clean(code));

    public static bool IsBuildingCode(string? code) => code is not null && BuildingCodeFormat().IsMatch(Clean(code));

    /// <summary>How every code of the quarter a day falls in starts: ID26Q4.</summary>
    public static string PeriodOf(DateOnly day) => $"ID{day.Year % 100:00}Q{(day.Month - 1) / 3 + 1}";

    /// <summary>The next code of a day's quarter: one past the highest number used in it, so a gap left by an edited code stays.</summary>
    public static string Next(DateOnly day, IEnumerable<string> used)
    {
        var period = PeriodOf(day);
        var highest = used
            .Where(code => code.StartsWith(period, StringComparison.Ordinal) && IsProjectCode(code))
            .Select(code => int.Parse(code[period.Length..], CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max();
        return $"{period}{highest + 1:000}";
    }

    /// <summary>
    /// The initials of a name: each word's first letter without its accent, or the whole of a number, in capitals, up to
    /// 10. Words are split at anything but a letter or a digit. "Villa Kadmous" is VK, "Mila 2 Concrete" M2C.
    /// </summary>
    public static string BuildingCodeOf(string title)
    {
        var initials = new StringBuilder();
        foreach (var word in Words().Matches(title).Select(m => m.Value).Where(w => !SkippedWords.Contains(w)))
        {
            initials.Append(char.IsDigit(word[0])
                ? string.Concat(word.TakeWhile(char.IsDigit))
                : word[..1].Normalize(NormalizationForm.FormD)[..1]);
        }

        var code = string.Concat(initials.ToString().ToUpperInvariant().Where(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9'));
        return code.Length == 0 ? NoInitials : code[..Math.Min(code.Length, MaxBuildingCodeLength)];
    }

    public static string Full(string projectCode, string buildingCode) => $"{projectCode}-{buildingCode}";
}
