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
    /// What an hour of overtime is paid at, before the company's multiplier: hourly pay's own rate, or the rate entered
    /// with a salary (the salary ÷ 184 unless an admin typed another, <c>PayRules.SuggestedOvertimeRate</c>). A salary's
    /// other hours are worth what the month gives (<c>PayRules.HourlyRate</c>).
    /// </summary>
    public decimal OvertimeRate { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public User User { get; set; } = null!;
}
