using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// Dated pay terms. The row in force on a day (latest <see cref="EffectiveFrom"/> on or before it)
/// costs the work done that day. Changes apply forward only; past rows are never edited.
/// </summary>
public class UserPayHistory
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public PayBasis Basis { get; set; }

    /// <summary>Hourly rate or monthly salary, depending on <see cref="Basis"/>.</summary>
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Hourly pay's rate (the <see cref="Amount"/>). Not used for monthly pay, whose hourly rate depends on the month and
    /// is worked out from the schedule (<c>PayRules.HourlyRate</c>): 0 on monthly rows, or salary ÷ 173.33 on rows made
    /// before that rule.
    /// </summary>
    public decimal HourlyCostRate { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public User User { get; set; } = null!;
}
