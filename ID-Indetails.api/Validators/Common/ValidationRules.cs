using FluentValidation;

namespace ID_Indetails.api.Validators.Common;

/// <summary>Field rules shared by several requests, so they are identical everywhere.</summary>
public static class ValidationRules
{
    public const int PasswordMinLength = 10;
    public const int PasswordMaxLength = 128;

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule)
        => rule
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(254).WithMessage("Email must be at most 254 characters.")
            .EmailAddress().WithMessage("Enter a valid email address.");

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
}
