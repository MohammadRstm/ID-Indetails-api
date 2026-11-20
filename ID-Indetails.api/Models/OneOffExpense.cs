namespace ID_Indetails.api.Models;

/// <summary>
/// A company cost paid once (a refrigerator, a repair), charged in full on the day it was spent. Like overhead it
/// belongs to the company, never to a project or a domain. It can be changed or removed only while its month is the
/// current one, so a closed month's profit never changes.
/// </summary>
public class OneOffExpense
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public long GroupId { get; set; }

    public OverheadGroup Group { get; set; } = null!;

    public string Currency { get; set; } = "USD";

    public decimal Amount { get; set; }

    public DateOnly SpentOn { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
