namespace ID_Indetails.api.Models;

/// <summary>
/// The company-wide default working week, break and overtime multiplier, dated so that a
/// change applies from its effective date and never rewrites past months. The row in force
/// on a day is the latest one with <see cref="EffectiveFrom"/> on or before it. People with a
/// personal schedule (<see cref="UserSchedule"/>) use it for their days and hours instead,
/// but the break and multiplier always come from here.
/// </summary>
public class WorkingTimePolicy
{
    public long Id { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Minutes after midnight, e.g. 540 = 09:00.</summary>
    public short WorkStartMinute { get; set; }

    /// <summary>Minutes after midnight, e.g. 1020 = 17:00.</summary>
    public short WorkEndMinute { get; set; }

    /// <summary>Working weekdays as a bitmask: bit 0 = Sunday … bit 6 = Saturday (62 = Monday–Friday).</summary>
    public byte WorkDays { get; set; }

    /// <summary>
    /// Daily break, taken whenever suits. It is <b>paid</b>: it only reduces the hours that are
    /// required, never pay. Applies on days longer than 6 hours.
    /// </summary>
    public short BreakMinutes { get; set; }

    /// <summary>Pay multiplier for overtime (1 = paid at the normal rate).</summary>
    public decimal OvertimeMultiplier { get; set; } = 1m;

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }
}
