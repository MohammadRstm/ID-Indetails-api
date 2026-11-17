namespace ID_Indetails.api.Models;

/// <summary>Someone a general meeting is for, or someone invited to a project meeting.</summary>
public class MeetingAttendee
{
    public long MeetingId { get; set; }

    public long UserId { get; set; }

    public ProjectMeeting Meeting { get; set; } = null!;

    public User User { get; set; } = null!;
}
