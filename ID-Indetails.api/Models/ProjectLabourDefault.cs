namespace ID_Indetails.api.Models;

/// <summary>
/// Execution only: dated default crew size and day rate used to pre-fill weekly labour
/// reports. Changing it never alters weeks already submitted.
/// </summary>
public class ProjectLabourDefault
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public int Workers { get; set; }

    public decimal DailyPay { get; set; }

    public string Currency { get; set; } = "USD";

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public Project Project { get; set; } = null!;
}
