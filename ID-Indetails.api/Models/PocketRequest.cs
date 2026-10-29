using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A site asking the pocket for money. The system suggests what brings its margin back to the warning level; a super
/// admin approves an amount of their choosing, or denies it. Decided once.
/// </summary>
public class PocketRequest
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public PocketRequestStatus Status { get; set; } = PocketRequestStatus.Pending;

    /// <summary>USD the system suggested when the request was made.</summary>
    public decimal SuggestedAmount { get; set; }

    /// <summary>USD paid out, once approved.</summary>
    public decimal? ApprovedAmount { get; set; }

    public string? Note { get; set; }

    public DateTime RequestedAt { get; set; }

    /// <summary>Null when the system raised it.</summary>
    public long? RequestedById { get; set; }

    public DateTime? DecidedAt { get; set; }

    public long? DecidedById { get; set; }

    public string? DecisionNote { get; set; }

    public Project Project { get; set; } = null!;
}
