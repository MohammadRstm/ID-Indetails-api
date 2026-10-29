using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A bill of quantities on an Execution client project: an admin's internal draft, the client BOQ, or a variation order
/// (VO). Agreeing the client BOQ and the VOs sets the project's work price. The totals are stored but always recomputed by
/// <c>BoqRules</c> from the lines on every save, never taken from the client. Drafts are soft-deleted so what was sent
/// stays on record; an agreed BOQ is never deleted.
/// </summary>
public class Boq
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public BoqKind Kind { get; set; }

    public BoqStatus Status { get; set; } = BoqStatus.Draft;

    /// <summary>BQ-YYYY-NNN for a client BOQ, VO-YYYY-NNN for a variation order; null for an internal draft.</summary>
    public string? Number { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Why a variation order was made. Null for any other kind.</summary>
    public string? Reason { get; set; }

    public long AuthorId { get; set; }

    public BoqAdjustmentType AdjustmentType { get; set; } = BoqAdjustmentType.None;

    /// <summary>A fraction for a percent adjustment, the new total for a total one; null for none.</summary>
    public decimal? AdjustmentValue { get; set; }

    /// <summary>The sum of the line totals, before the adjustment.</summary>
    public decimal SubtotalUsd { get; set; }

    /// <summary>The subtotal after the adjustment: what the client pays for the work, before supervision and TVA.</summary>
    public decimal TotalUsd { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? AgreedAt { get; set; }

    public long? AgreedById { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Project Project { get; set; } = null!;

    public ICollection<BoqLine> Lines { get; set; } = new List<BoqLine>();

    public ICollection<BoqDelivery> Deliveries { get; set; } = new List<BoqDelivery>();
}
