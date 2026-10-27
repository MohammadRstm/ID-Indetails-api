using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// One movement of the Execution box, the pool of money its projects' clients paid. The balance is the sum of every
/// <see cref="Amount"/>: positive in, negative out. Never edited or deleted.
/// </summary>
public class BoxEntry
{
    public long Id { get; set; }

    public BoxEntryKind Kind { get; set; }

    /// <summary>The project the money came from, went to or was taken out for.</summary>
    public long ProjectId { get; set; }

    /// <summary>The client payment, for <see cref="BoxEntryKind.PaymentIn"/> and <see cref="BoxEntryKind.PaymentVoided"/>.</summary>
    public long? PaymentId { get; set; }

    /// <summary>USD, signed.</summary>
    public decimal Amount { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Null for entries the system wrote.</summary>
    public long? CreatedById { get; set; }

    public Project Project { get; set; } = null!;
}
