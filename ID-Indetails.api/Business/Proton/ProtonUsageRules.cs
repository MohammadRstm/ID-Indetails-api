namespace ID_Indetails.api.Business.Proton;

/// <summary>Tokens of one or more model calls, as the provider reported them.</summary>
public sealed record ProtonTokens(int Input, int Output, int CacheRead, int CacheWrite)
{
    public static ProtonTokens None { get; } = new(0, 0, 0, 0);

    /// <summary>The prompt's length, cached or not: what picks the rate card.</summary>
    public int Prompt => Input + CacheRead + CacheWrite;

    public ProtonTokens Add(ProtonTokens other)
        => new(Input + other.Input, Output + other.Output, CacheRead + other.CacheRead, CacheWrite + other.CacheWrite);
}

/// <summary>
/// USD per million tokens. A model call whose prompt is longer than <see cref="LongPromptAbove"/> tokens is priced at
/// <see cref="LongPrompt"/> instead, when the model has a second rate card.
/// </summary>
public sealed record ProtonTokenPrices(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite)
{
    public int LongPromptAbove { get; init; }

    public ProtonTokenPrices? LongPrompt { get; init; }
}

/// <summary>A month's allowance (budget in USD, or questions) against what was used. Never below zero remaining.</summary>
public sealed record ProtonAllowance(decimal Limit, decimal Used)
{
    public decimal Remaining => Math.Max(0, Limit - Used);

    public int UsedPct => Limit <= 0 ? 100 : (int)Math.Min(100, Math.Floor(Used / Limit * 100));

    public bool IsExhausted => Used >= Limit;
}

/// <summary>
/// How Proton's usage is priced and limited. The cost is an estimate from the configured prices (the provider's bill
/// is the truth). A month is the company-time-zone calendar month. Asking is refused once either the company budget
/// (configured monthly budget + that month's top-ups) or the person's monthly question allowance is used up.
/// </summary>
public static class ProtonUsageRules
{
    private const decimal Million = 1_000_000m;

    /// <summary>One model call's cost: the call's own prompt length picks the rate card.</summary>
    public static decimal CostOf(ProtonTokens call, ProtonTokenPrices prices)
    {
        var card = prices.LongPrompt is { } longPrompt && call.Prompt > prices.LongPromptAbove ? longPrompt : prices;
        return Math.Round(
            (call.Input * card.Input
             + call.Output * card.Output
             + call.CacheRead * card.CacheRead
             + call.CacheWrite * card.CacheWrite) / Million,
            6);
    }

    public static DateOnly MonthOf(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>Whether a question may hold <paramref name="amount"/> on top of what questions being answered already hold.</summary>
    public static bool CanHold(decimal remaining, decimal held, decimal amount) => held + amount <= remaining;

    public static ProtonAllowance Budget(decimal monthlyBudget, decimal topUps, decimal spent) => new(monthlyBudget + topUps, spent);

    public static ProtonAllowance Questions(int perUser, int asked) => new(perUser, asked);
}
