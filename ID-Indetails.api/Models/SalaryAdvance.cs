using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// Part of someone's pay handed over early. It costs the company nothing more: it comes off what the person is handed
/// in the months of its <see cref="Repayments"/>. Asked for by the person and approved by a super admin, or recorded
/// directly by one. What is left can be closed by cash or written off (<see cref="SettledKind"/>).
/// </summary>
public class SalaryAdvance
{
    public long Id { get; set; }

    public long UserId { get; set; }

    /// <summary>What was given; while pending, what is asked for.</summary>
    public decimal Amount { get; set; }

    /// <summary>What the person asked for; null when a super admin recorded it directly.</summary>
    public decimal? AskedAmount { get; set; }

    /// <summary>The currency of the person's pay when it was asked for or recorded.</summary>
    public string Currency { get; set; } = "USD";

    public SalaryAdvanceStatus Status { get; set; } = SalaryAdvanceStatus.Pending;

    /// <summary>Why the person asks; null when a super admin recorded it directly.</summary>
    public string? Reason { get; set; }

    /// <summary>When it was asked for, or recorded.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>The person who asked, or the super admin who recorded it.</summary>
    public long CreatedById { get; set; }

    /// <summary>When it was approved, declined or withdrawn; when it was recorded, for one recorded directly.</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Who approved, declined or recorded it; the person, for a withdrawal.</summary>
    public long? DecidedById { get; set; }

    /// <summary>The super admin's note: why it was declined, or how the money was handed over.</summary>
    public string? DecisionNote { get; set; }

    /// <summary>The day the money was handed over. Set once approved or recorded.</summary>
    public DateOnly? GivenOn { get; set; }

    public DateTime? CancelledAt { get; set; }

    public long? CancelledById { get; set; }

    /// <summary>How what was left was closed; null while it is still being paid back from pay.</summary>
    public SalaryAdvanceSettlement? SettledKind { get; set; }

    /// <summary>What was left when it was settled: paid in cash, or written off as a company expense.</summary>
    public decimal? SettledAmount { get; set; }

    /// <summary>From this day's month on, nothing more comes off the person's pay.</summary>
    public DateOnly? SettledOn { get; set; }

    public long? SettledById { get; set; }

    public string? SettlementNote { get; set; }

    public User User { get; set; } = null!;

    public List<SalaryAdvanceRepayment> Repayments { get; set; } = [];
}
