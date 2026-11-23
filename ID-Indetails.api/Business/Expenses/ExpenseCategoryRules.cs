using System.Text;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Expenses;

/// <summary>
/// Each business's expense categories (23 November 2026): Design's for project expenses, Execution's for site expenses.
/// <list type="bullet">
/// <item>Expenses keep a category's code, made from its first name and never changed; the name can change.</item>
/// <item>A category any expense or budget line uses can't be removed: move those to another category first.</item>
/// <item>"Other" is every business's fallback: it can be renamed, never removed.</item>
/// </list>
/// </summary>
public static class ExpenseCategoryRules
{
    public const string Other = "other";

    /// <summary>A new site expense's category when none is given, while the business has it.</summary>
    public const string Materials = "materials";

    public const int MaxNameLength = 60;
    public const int MaxCodeLength = 20;

    public static bool HasCategories(string domain) => domain is DomainCodes.Design or DomainCodes.Execution;

    public static RuleDenial? CheckDomain(string domain)
        => HasCategories(domain)
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "domain", "Only Design and Execution have expense categories.");

    public static RuleDenial? CheckName(string? name, IEnumerable<string> otherNames)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "name", $"Give it a name of up to {MaxNameLength} characters.");
        }

        return otherNames.Any(n => string.Equals(n.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            ? RuleDenial.Invalid(ErrorCodes.ExpenseCategoryNameTaken, "name", "This business already has a category with that name.")
            : null;
    }

    /// <summary>A code from the name: lowercase letters and digits joined by underscores, unique in the business.</summary>
    public static string CodeFor(string name, IReadOnlyCollection<string> taken)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        var stem = builder.ToString().Trim('_');
        if (stem.Length == 0)
        {
            stem = "category";
        }

        stem = stem[..Math.Min(stem.Length, MaxCodeLength - 3)].TrimEnd('_');
        var code = stem;
        for (var n = 2; taken.Contains(code); n++)
        {
            code = $"{stem}_{n}";
        }

        return code;
    }

    public static RuleDenial? CheckRemovable(string code, int uses)
    {
        if (code == Other)
        {
            return RuleDenial.BusinessRule(ErrorCodes.ExpenseCategoryProtected, "\"Other\" is every business's fallback: rename it instead.");
        }

        return uses > 0
            ? RuleDenial.BusinessRule(ErrorCodes.ExpenseCategoryInUse,
                $"{uses} {(uses == 1 ? "expense or budget line uses" : "expenses or budget lines use")} this category: move them to another category first.")
            : null;
    }

    public static RuleDenial? CheckKnown(string? code, IReadOnlyCollection<string> codes, string field)
        => code is null || codes.Contains(code)
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, field, $"The category must be one of: {string.Join(", ", codes)}.");
}
