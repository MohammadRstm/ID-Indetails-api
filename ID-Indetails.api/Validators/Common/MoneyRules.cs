using FluentValidation;

namespace ID_Indetails.api.Validators.Common;

/// <summary>Shape of money fields: they must fit <c>DECIMAL(18,2)</c>, rates <c>DECIMAL(18,6)</c>, and currencies are ISO codes.</summary>
public static class MoneyRules
{
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    /// <summary>Exchange rates stay under a trillion, as the dated rates do.</summary>
    public const decimal MaxRate = 999_999_999_999.999999m;

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

    public static IRuleBuilderOptions<T, decimal?> Rate<T>(this IRuleBuilder<T, decimal?> rule)
        => rule
            .Must(r => r is null || (r > 0 && decimal.Round(r.Value, 6) == r)).WithMessage("Exchange rate must be more than zero with at most 6 decimals.")
            .Must(r => r is null || r <= MaxRate).WithMessage("Exchange rate is too large.");

    /// <summary>An amount at its rate (amount ÷ rate) is a US dollar amount too: it must fit as well.</summary>
    public static bool FitsInUsd(decimal amount, decimal? rate) => rate is not > 0 || amount / rate.Value <= MaxAmount;

    public static IRuleBuilderOptions<T, string> Currency<T>(this IRuleBuilder<T, string> rule)
        => rule.Must(c => c is not null && Currencies.Contains(c.ToUpperInvariant()))
            .WithMessage($"Currency must be one of: {string.Join(", ", Currencies)}.");
}
