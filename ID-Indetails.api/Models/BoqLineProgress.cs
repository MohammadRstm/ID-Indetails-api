namespace ID_Indetails.api.Models;

/// <summary>
/// How much of one BOQ line was executed on a site, up to a day: the cumulative quantity, in the line's unit, as site
/// staff or a pricing manager recorded it. One record per line and day; a second one the same day replaces it. Only lines
/// of an agreed client BOQ or VO take progress (<c>BoqProgressRules</c>); a line removed from its BOQ takes its records with it.
/// </summary>
public class BoqLineProgress
{
    public long Id { get; set; }

    public long BoqLineId { get; set; }

    /// <summary>The site, kept beside the line so a site's progress is read without going through its BOQs.</summary>
    public long ProjectId { get; set; }

    /// <summary>The company day the quantity stands at.</summary>
    public DateOnly RecordedOn { get; set; }

    /// <summary>Everything executed on the line up to <see cref="RecordedOn"/>, not that day's work alone.</summary>
    public decimal ExecutedQuantity { get; set; }

    public long RecordedById { get; set; }

    public DateTime RecordedAt { get; set; }

    public string? Note { get; set; }
}
