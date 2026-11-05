using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// One line of a BOQ. The client reads the final quantity (quantity plus waste), the unit, the rate and the total; the
/// exact quantity and the waste stay internal. Final quantity and total are calculated by <c>BoqRules</c>, not stored.
/// </summary>
public class BoqLine
{
    public long Id { get; set; }

    public long BoqId { get; set; }

    public int Position { get; set; }

    /// <summary>Work, or a permit: a permit carries no supervision and no TVA.</summary>
    public BoqLineNature Nature { get; set; } = BoqLineNature.Work;

    /// <summary>The line's number in the BOQ, e.g. "2.1.1".</summary>
    public string? Code { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>The exact quantity, before waste.</summary>
    public decimal Quantity { get; set; }

    /// <summary>A fraction: 0.05 adds 5% to the quantity.</summary>
    public decimal WasteRate { get; set; }

    public string? Unit { get; set; }

    /// <summary>Negative only on a VO, so it can lower the price.</summary>
    public decimal Rate { get; set; }

    public Boq Boq { get; set; } = null!;
}
