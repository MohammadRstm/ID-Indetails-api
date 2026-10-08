using System.Text.RegularExpressions;

namespace ID_Indetails.api.Business.Proton;

/// <summary>
/// What may be remembered for someone (decision 20c): a short preference in their own words. Not instructions about
/// tools or changes, not people or whom to message, and nothing secret: no addresses, numbers, pay or credentials.
/// </summary>
public static partial class ProtonMemoryRules
{
    public const int MaxLength = 300;

    public const int MaxPerUser = 20;

    /// <summary>Why <paramref name="text"/> can't be remembered, in words; null when it can.</summary>
    /// <param name="people">Names of the people the user can see: a memory naming one is refused.</param>
    public static string? Problem(string text, IEnumerable<string> people)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength)
        {
            return $"A memory is 1 to {MaxLength} characters.";
        }

        if (ToolWords().IsMatch(trimmed))
        {
            return "I can remember how you like answers, not instructions to make changes or send things.";
        }

        if (Secrets().IsMatch(trimmed))
        {
            return "I don't remember addresses, numbers, pay or passwords.";
        }

        var named = people.Where(p => p.Length >= 3).FirstOrDefault(p => trimmed.Contains(p, StringComparison.OrdinalIgnoreCase));
        return named is null ? null : "I don't remember things about other people.";
    }

    [GeneratedRegex(@"propose_|\b(confirm|approve|send|delete|void|pay|transfer|invite|deactivate|always (do|say yes)|without asking|skip)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ToolWords();

    [GeneratedRegex(@"@|\d{4,}|\b(password|passcode|token|iban|swift|salary|wage|bank|card)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Secrets();
}
