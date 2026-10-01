namespace ID_Indetails.api.Models;

/// <summary>
/// A recurring monthly company cost. It belongs to the company, never to a project or a
/// domain, and is subtracted once above both domains.
/// </summary>
public class OverheadItem
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public long GroupId { get; set; }

    public OverheadGroup Group { get; set; } = null!;

    public string Currency { get; set; } = "USD";

    public DateOnly StartedOn { get; set; }

    /// <summary>Null while the cost is still running.</summary>
    public DateOnly? EndedOn { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public ICollection<OverheadAmount> Amounts { get; set; } = new List<OverheadAmount>();
}
