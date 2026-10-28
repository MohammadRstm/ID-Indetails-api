namespace ID_Indetails.api.Models;

/// <summary>
/// A recorded meeting: a project meeting, which everyone who sees the project sees, or a general one with no project,
/// which is for its attendees. Its display title is derived from the start time, never stored.
/// </summary>
public class ProjectMeeting
{
    public long Id { get; set; }

    /// <summary>Null for a general meeting.</summary>
    public long? ProjectId { get; set; }

    /// <summary>
    /// A general meeting's domain, whose meeting managers see it; null for a company-wide one, or a project meeting
    /// (which belongs to its project's domain).
    /// </summary>
    public string? DomainCode { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public string? Topic { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    /// <summary>Meetings can be moved or resized after creation.</summary>
    public DateTime? UpdatedAt { get; set; }

    public long? UpdatedById { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Project? Project { get; set; }

    /// <summary>A general meeting's people; a project meeting has none.</summary>
    public ICollection<MeetingAttendee> Attendees { get; set; } = new List<MeetingAttendee>();
}
