using System.Text.RegularExpressions;
using FluentValidation;

namespace ID_Indetails.api.Validators.Common;

/// <summary>Field rules shared by several requests, so they are identical everywhere.</summary>
public static partial class ValidationRules
{
    public const int PasswordMinLength = 10;
    public const int MaxPage = 10_000;
    public static readonly DateOnly EarliestDate = new(2000, 1, 1);
    public static readonly DateOnly LatestDate = new(2100, 12, 31);
    public const int PasswordMaxLength = 128;

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule)
        => rule
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(254).WithMessage("Email must be at most 254 characters.")
            .Must(IsPlainEmail).WithMessage("Enter a valid email address.");

    /// <summary>
    /// One address: a name, an @ and a domain with a dot, with no spaces, control characters or the characters that
    /// would add headers or recipients to a mailto link (? % , ; &lt; &gt;).
    /// </summary>
    public static bool IsPlainEmail(string? value) => value is not null && PlainEmail().IsMatch(value.Trim());

    public static IRuleBuilderOptions<T, int> ValidPage<T>(this IRuleBuilder<T, int> rule)
        => rule.InclusiveBetween(1, MaxPage).WithMessage($"Page must be between 1 and {MaxPage}.");

    public static IRuleBuilderOptions<T, DateOnly> ThisCentury<T>(this IRuleBuilder<T, DateOnly> rule, string label)
        => rule.InclusiveBetween(EarliestDate, LatestDate).WithMessage($"{label} must be between 2000 and 2100.");

    public static IRuleBuilderOptions<T, DateOnly?> ThisCentury<T>(this IRuleBuilder<T, DateOnly?> rule, string label)
        => rule.Must(d => d is null || (d >= EarliestDate && d <= LatestDate)).WithMessage($"{label} must be between 2000 and 2100.");

    /// <summary>
    /// Length is what makes a password strong; no composition rules (NIST SP 800-63B).
    /// The upper bound stops very long inputs being used to slow the hasher.
    /// </summary>
    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilder<T, string> rule)
        => rule
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(PasswordMinLength).WithMessage($"Password must be at least {PasswordMinLength} characters.")
            .MaximumLength(PasswordMaxLength).WithMessage($"Password must be at most {PasswordMaxLength} characters.");

    public static IRuleBuilderOptions<T, string> PersonName<T>(this IRuleBuilder<T, string> rule, string label)
        => rule
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage($"{label} is required.")
            .MaximumLength(100).WithMessage($"{label} must be at most 100 characters.");

    [GeneratedRegex(@"^[^\s@?%,;<>()\[\]\\""]+@[^\s@?%,;<>()\[\]\\""]+\.[^\s@?%,;<>()\[\]\\""]+$")]
    private static partial Regex PlainEmail();
}
