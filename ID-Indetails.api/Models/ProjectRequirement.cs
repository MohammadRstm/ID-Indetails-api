namespace ID_Indetails.api.Models;

/// <summary>A deliverable of a project. Done / total requirements is the project's progress.</summary>
public class ProjectRequirement
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateOnly? Deadline { get; set; }

    public bool IsDone { get; set; }

    public DateTime? DoneAt { get; set; }

    public long? DoneById { get; set; }

    /// <summary>Ticked automatically by completing the project; reopening unticks exactly these.</summary>
    public bool AutoDone { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Project Project { get; set; } = null!;

    public ICollection<RequirementAssignee> Assignees { get; set; } = new List<RequirementAssignee>();
}
