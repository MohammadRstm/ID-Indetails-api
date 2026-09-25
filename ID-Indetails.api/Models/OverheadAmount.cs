namespace ID_Indetails.api.Models;

/// <summary>
/// The monthly amount of an overhead line from a date onward. Dated so that changing the
/// amount never rewrites past months' company profit.
/// </summary>
public class OverheadAmount
{
    public long Id { get; set; }

    public long OverheadItemId { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public decimal Amount { get; set; }

    public DateTime CreatedAt { get; set; }

    public long? CreatedById { get; set; }

    public OverheadItem OverheadItem { get; set; } = null!;
}
