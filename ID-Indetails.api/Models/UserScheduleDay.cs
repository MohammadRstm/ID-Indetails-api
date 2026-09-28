namespace ID_Indetails.api.Models;

/// <summary>One working weekday of a personal schedule: a single span within the day.</summary>
public class UserScheduleDay
{
    public long ScheduleId { get; set; }

    /// <summary>0 = Sunday … 6 = Saturday.</summary>
    public byte Weekday { get; set; }

    /// <summary>Minutes after midnight.</summary>
    public short StartMinute { get; set; }

    /// <summary>Minutes after midnight, after <see cref="StartMinute"/>.</summary>
    public short EndMinute { get; set; }

    public UserSchedule Schedule { get; set; } = null!;
}
