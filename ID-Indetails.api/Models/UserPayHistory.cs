namespace ID_Indetails.api.Models;

/// <summary>
/// Dated pay terms. The active row in force on a day (latest <see cref="EffectiveFrom"/> on or before it) pays and costs
/// that day. A change dated D replaces every active row from D onward: they are marked superseded and kept for the
/// record, and a global query filter hides them from everything else.
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

    /// <summary>When a later change dated on or before this row's day replaced it; null while it is active.</summary>
    public DateTime? SupersededAt { get; set; }

    public long? SupersededById { get; set; }

    /// <summary><see cref="EffectiveFrom"/> while active, null once superseded: unique per person, worked out by the database.</summary>
    public DateOnly? ActiveEffectiveFrom { get; private set; }

    public User User { get; set; } = null!;
}
