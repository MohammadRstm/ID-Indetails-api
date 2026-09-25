namespace ID_Indetails.api.Models;

/// <summary>
/// Execution only: one weekly ground-labour report per project per week. Resubmitting
/// replaces the days of the existing report.
/// </summary>
public class LabourWeek
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    /// <summary>Monday of the reported week.</summary>
    public DateOnly WeekStart { get; set; }

    public string? Note { get; set; }

    public DateTime SubmittedAt { get; set; }

    public long SubmittedById { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Project Project { get; set; } = null!;

    public ICollection<LabourWeekDay> Days { get; set; } = new List<LabourWeekDay>();
}
