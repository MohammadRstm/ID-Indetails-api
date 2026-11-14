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

    /// <summary>The monthly salary: everyone is paid one.</summary>
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>
    /// What an hour of overtime is paid at, before the company's multiplier: the rate entered with the salary (the salary
    /// ÷ 184 unless an admin typed another, <c>PayRules.SuggestedOvertimeRate</c>). Its other hours are worth what the
    /// month gives (<c>PayRules.HourlyRate</c>).
    /// </summary>
    public decimal OvertimeRate { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public User User { get; set; } = null!;
}
