namespace ID_Indetails.api.Models;

/// <summary>A recorded project meeting. Its display title is derived from the start time, never stored.</summary>
public class ProjectMeeting
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public string? Topic { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Project Project { get; set; } = null!;
}
