using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// One movement of Execution's pocket money. The balance is the sum of every <see cref="Amount"/>: a contribution is
/// positive (into the pocket), a payout negative. A contribution lowers its site's profit and a payout raises it;
/// neither changes what the client owes. Never edited or deleted.
/// </summary>
public class PocketEntry
{
    public long Id { get; set; }

    public PocketEntryKind Kind { get; set; }

    public long ProjectId { get; set; }

    /// <summary>USD, from the pocket's side: positive in, negative out.</summary>
    public decimal Amount { get; set; }

    /// <summary>The approved request a payout answers.</summary>
    public long? RequestId { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public long CreatedById { get; set; }

    public Project Project { get; set; } = null!;
}
