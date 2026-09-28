using FluentValidation;

namespace ID_Indetails.api.Validators.Common;

/// <summary>Shape of money fields: they must fit <c>DECIMAL(18,2)</c> and currencies are ISO codes.</summary>
public static class MoneyRules
{
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    /// <summary>Currencies the company deals in.</summary>
    public static readonly IReadOnlyList<string> Currencies = ["USD", "LBP"];

    public static IRuleBuilderOptions<T, decimal> Amount<T>(this IRuleBuilder<T, decimal> rule, string label, bool allowZero = true)
        => rule
            .Must(v => allowZero ? v >= 0 : v > 0).WithMessage(allowZero ? $"{label} can't be negative." : $"{label} must be more than zero.")
            .Must(v => v <= MaxAmount).WithMessage($"{label} is too large.")
            .Must(v => decimal.Round(v, 2) == v).WithMessage($"{label} can have at most 2 decimals.");

    public static IRuleBuilderOptions<T, decimal?> Amount<T>(this IRuleBuilder<T, decimal?> rule, string label, bool allowZero = true)
        => rule
            .Must(v => v is null || (allowZero ? v >= 0 : v > 0)).WithMessage(allowZero ? $"{label} can't be negative." : $"{label} must be more than zero.")
            .Must(v => v is null || v <= MaxAmount).WithMessage($"{label} is too large.")
            .Must(v => v is null || decimal.Round(v.Value, 2) == v).WithMessage($"{label} can have at most 2 decimals.");

    public static IRuleBuilderOptions<T, string> Currency<T>(this IRuleBuilder<T, string> rule)
        => rule.Must(c => c is not null && Currencies.Contains(c.ToUpperInvariant()))
            .WithMessage($"Currency must be one of: {string.Join(", ", Currencies)}.");
}
