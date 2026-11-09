using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A Design employee asking for whole days off, from <see cref="FirstDay"/> to <see cref="LastDay"/>. A super admin
/// approves it, with pay or without, or declines it; once approved it can still be rejected until its last day.
/// </summary>
public class TimeOffRequest
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public DateOnly FirstDay { get; set; }

    public DateOnly LastDay { get; set; }

    public string Reason { get; set; } = string.Empty;

    public TimeOffStatus Status { get; set; } = TimeOffStatus.Pending;

    /// <summary>Set once approved: whether the days off are paid.</summary>
    public bool? IsPaid { get; set; }

    public DateTime RequestedAt { get; set; }

    /// <summary>When it was approved, declined, withdrawn or expired.</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Who approved or declined it; the employee for a withdrawal, nobody when it expired.</summary>
    public long? DecidedById { get; set; }

    public string? DecisionNote { get; set; }

    /// <summary>
    /// Rejected or ended: the first day that is no longer time off. The days before it (from <see cref="FirstDay"/>) were
    /// taken; it and the days after are work days again.
    /// </summary>
    public DateOnly? EndedOn { get; set; }

    public DateTime? EndedAt { get; set; }

    /// <summary>Who rejected it; whoever made the change that ended it.</summary>
    public long? EndedById { get; set; }

    public string? EndNote { get; set; }

    public User User { get; set; } = null!;
}
