namespace ID_Indetails.api.Models;

/// <summary>Default exchange rate offered when recording a non-USD payment. Dated; never edited.</summary>
public class ExchangeRate
{
    public long Id { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>Units of <see cref="Currency"/> per USD.</summary>
    public decimal RatePerUsd { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }
}
