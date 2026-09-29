namespace ID_Indetails.api.Models;

/// <summary>
/// A stretch of someone's employment. Accepting their invitation starts the first one, leaving ends it on their last
/// working day, and being taken back starts a new one. Pay and the hours ledger count only the days inside one.
/// </summary>
public class EmploymentPeriod
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public DateOnly StartedOn { get; set; }

    /// <summary>The last working day; null while it lasts.</summary>
    public DateOnly? EndedOn { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public User User { get; set; } = null!;
}
