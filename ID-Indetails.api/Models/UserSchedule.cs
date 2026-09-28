namespace ID_Indetails.api.Models;

/// <summary>
/// A person's own weekly working pattern from <see cref="EffectiveFrom"/> onward. Dated, so
/// changing it never alters past overtime, hours or pay. A row with
/// <see cref="FollowsDefault"/> records a return to the company working week.
/// </summary>
public class UserSchedule
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>True: from this date the person follows the company default (and has no days here).</summary>
    public bool FollowsDefault { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public User User { get; set; } = null!;

    /// <summary>Working days only. A weekday with no row is a day off.</summary>
    public ICollection<UserScheduleDay> Days { get; set; } = new List<UserScheduleDay>();
}
